import { execFileSync } from "node:child_process";
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { analyzeCommits } from "@semantic-release/commit-analyzer";
import { generateNotes } from "@semantic-release/release-notes-generator";
import conventionalCommits from "conventional-changelog-conventionalcommits";
import { CommitParser } from "conventional-commits-parser";
import semver from "semver";

const repository = process.env.GITHUB_REPOSITORY || "Stephanvs/archevolt";
const repositoryUrl = `https://github.com/${repository}`;
const apiUrl = process.env.GITHUB_API_URL || "https://api.github.com/";
const root = process.cwd();
const releaseDirectory = resolve("artifacts/release");
const canonicalTag = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/u;
const logger = { log() {}, error() {} };

function command(file, args, options = {}) {
  return execFileSync(file, args, { cwd: root, encoding: "utf8", maxBuffer: 16 * 1024 * 1024, ...options });
}

function git(...args) {
  return command("git", args).trim();
}

async function github(path, { method = "GET", body, binary = false } = {}) {
  const headers = { Connection: "close", Accept: binary ? "application/octet-stream" : "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28" };
  const token = process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body) headers["Content-Type"] = Buffer.isBuffer(body) ? "application/octet-stream" : "application/json";
  const response = await fetch(new URL(path, `${apiUrl.replace(/\/$/u, "")}/`), {
    method, headers, body: body && (Buffer.isBuffer(body) ? body : JSON.stringify(body)), signal: AbortSignal.timeout(60_000),
  });
  if (!response.ok) throw new Error(`GitHub ${method} ${new URL(response.url).pathname} returned HTTP ${response.status}.`);
  return binary ? Buffer.from(await response.arrayBuffer()) : response.json();
}

function isAncestor(ancestor, descendant) {
  try {
    git("merge-base", "--is-ancestor", ancestor, descendant);
    return true;
  } catch (error) {
    if (error.status === 1) return false;
    throw error;
  }
}

function commitsSince(previousTag, sourceSha) {
  const range = previousTag ? `${previousTag}..${sourceSha}` : sourceSha;
  const fields = command("git", ["log", "--format=%H%x00%B%x00", range]).split("\0");
  const commits = [];
  for (let index = 0; index + 1 < fields.length; index += 2) {
    commits.push({ hash: fields[index].trim(), message: fields[index + 1].trim() });
  }
  return commits;
}

async function newPlan(sourceSha, previousTag) {
  const commits = commitsSince(previousTag, sourceSha);
  if (commits.length === 0) throw new Error("The release range contains no commits.");
  const preset = conventionalCommits();
  const parser = new CommitParser(preset.parser);
  const parsed = commits.map(({ message }) => parser.parse(message));
  const recommended = await analyzeCommits({ preset: "conventionalcommits" }, { cwd: root, commits, logger });
  const bump = parsed.some((commit) => commit.notes.length > 0) ? "major"
    : parsed.some((commit) => commit.type === "feat") ? "minor" : recommended || "patch";
  const version = previousTag ? semver.inc(previousTag.slice(1), bump) : "0.1.0";
  const types = [
    ["feat", "Features"], ["fix", "Fixes"], ["perf", "Performance"],
    ["refactor", "Refactoring"], ["docs", "Documentation"], ["test", "Tests"],
    ["build", "Build"], ["ci", "CI"], ["chore", "Maintenance"],
    ["style", "Style"], ["revert", "Reverts"],
  ].map(([type, section]) => ({ type, section, hidden: false }));
  let notes = (await generateNotes(
    { preset: "conventionalcommits", presetConfig: { types } },
    {
      cwd: root, commits, logger, options: { repositoryUrl },
      lastRelease: { gitTag: previousTag || undefined },
      nextRelease: { version, gitTag: `v${version}`, gitHead: sourceSha },
    },
  )).trim();
  const omitted = commits.filter(({ hash }) => !notes.includes(`/commit/${hash}`));
  if (omitted.length) {
    const bullets = omitted.map(({ hash, message }) => {
      const subject = message.split("\n")[0].replaceAll(/[\\`*_{}\[\]<>]/gu, "\\$&");
      return `* ${subject} ([${hash.slice(0, 7)}](${repositoryUrl}/commit/${hash}))`;
    });
    notes += `\n\n### Other changes\n\n${bullets.join("\n")}`;
  }
  return { sourceSha, previousTag, version, notes };
}

async function releaseState() {
  const sourceSha = git("rev-parse", "HEAD");
  const tags = git("tag", "--list", "v*").split("\n").filter((tag) => canonicalTag.test(tag));
  const releases = [];
  for (let page = 1; ; page++) {
    const batch = await github(`repos/${repository}/releases?per_page=100&page=${page}`);
    releases.push(...batch);
    if (batch.length < 100) break;
  }
  const states = tags.map((tag) => ({
    tag,
    sourceSha: git("rev-parse", `${tag}^{commit}`),
    release: releases.find((release) => release.tag_name === tag),
  })).sort((left, right) => semver.compare(left.tag.slice(1), right.tag.slice(1)));
  for (const release of releases.filter((item) => canonicalTag.test(item.tag_name))) {
    if (!tags.includes(release.tag_name)) {
      throw new Error(`Release ${release.tag_name} has no local tag. Fetch the complete origin history and tags.`);
    }
  }
  const published = states.filter(({ release }) => release && !release.draft);
  const latest = published.at(-1);
  if (latest && isAncestor(sourceSha, latest.sourceSha)) {
    return { kind: "published", ...latest };
  }
  if (latest && !isAncestor(latest.sourceSha, sourceSha)) {
    throw new Error(`HEAD ${sourceSha} diverges from published ${latest.tag} at ${latest.sourceSha}.`);
  }
  const pending = states.filter(({ release }) => !release || release.draft);
  if (pending.length > 1 || pending.some((state) => state.sourceSha !== sourceSha)) {
    const reserved = pending.map((state) => `${state.tag} at ${state.sourceSha}`).join(", ");
    throw new Error(`Unfinished release ${reserved}. Rerun its original release.yml run at that SHA before releasing a later commit. Do not move or delete its tag.`);
  }
  const previousTag = latest?.tag || null;
  if (pending.length) {
    const reserved = pending[0];
    if (git("cat-file", "-t", reserved.tag) !== "tag") {
      throw new Error(`${reserved.tag} is not an annotated release tag. Inspect it before recovery.`);
    }
    if (previousTag && !semver.gt(reserved.tag.slice(1), previousTag.slice(1))) {
      throw new Error(`${reserved.tag} does not follow published ${previousTag}. Inspect the release history.`);
    }
    const annotation = git("for-each-ref", "--format=%(contents)", `refs/tags/${reserved.tag}`);
    const binding = /^(?<notes>[\s\S]+)\n\nArchevolt-Package-SHA256: (?<digest>[0-9a-f]{64})(?:\nArchevolt-Symbols-SHA256: (?<symbolsDigest>[0-9a-f]{64}))?$/u.exec(annotation);
    if (!binding) throw new Error(`${reserved.tag} has no valid package content digest. Inspect it before recovery.`);
    return {
      kind: "reserved", ...reserved, packageDigest: binding.groups.digest, symbolsDigest: binding.groups.symbolsDigest,
      plan: { sourceSha, previousTag, version: reserved.tag.slice(1), notes: binding.groups.notes },
    };
  }
  return { kind: "new", plan: await newPlan(sourceSha, previousTag) };
}

function output(values) {
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join(""));
  }
  console.log(JSON.stringify(values, null, 2));
}

function verifyPackage(path, plan, actual, expectedDigest) {
  return command("pwsh", [
    "-NoProfile", "-File", join(import.meta.dirname, "verify-package.ps1"),
    "-Package", path, "-PackageVersion", plan.version, "-SourceRevisionId", plan.sourceSha,
    "-Fingerprint",
    ...(actual ? ["-CompareWith", actual] : []),
    ...(expectedDigest ? ["-ExpectedDigest", expectedDigest] : []),
  ]).trim();
}

function releasePackages(state) {
  const packages = [{ extension: "nupkg", digest: state.packageDigest }];
  if (state.kind === "new" || state.symbolsDigest) {
    packages.push({ extension: "snupkg", digest: state.symbolsDigest });
  }
  return packages.map((entry) => ({ ...entry, name: `Archevolt.${state.plan.version}.${entry.extension}` }));
}

async function downloadAsset(release, plan, { name, digest }) {
  const asset = release.assets.find((entry) => entry.name === name);
  if (!asset) return null;
  const path = join(releaseDirectory, name);
  const bytes = await github(`repos/${repository}/releases/assets/${asset.id}`, { binary: true });
  writeFileSync(path, bytes);
  verifyPackage(path, plan, undefined, digest);
  return path;
}

async function prepare() {
  const state = await releaseState();
  if (state.kind === "published") {
    output({ publish: false, tag: state.tag });
    return;
  }
  if (git("status", "--porcelain")) {
    throw new Error("The release source has uncommitted files. Use a clean checkout of the reserved source commit.");
  }
  mkdirSync(releaseDirectory, { recursive: true });
  const { plan } = state;
  const tag = `v${plan.version}`;
  let release = state.release;
  const packages = releasePackages(state);
  for (const entry of packages) {
    entry.path = release ? await downloadAsset(release, plan, entry) : null;
  }
  if (packages.some((entry) => !entry.path)) {
    command("pwsh", ["-NoProfile", "-File", join(import.meta.dirname, "verify.ps1"), "-PackageVersion", plan.version, "-SourceRevisionId", plan.sourceSha], { stdio: "inherit" });
    for (const entry of packages) {
      const builtPath = resolve("artifacts/packages", entry.name);
      entry.digest = verifyPackage(builtPath, plan, undefined, entry.digest);
      entry.path ||= builtPath;
    }
  }
  if (state.kind === "new") {
    const notesPath = join(releaseDirectory, "notes.md");
    writeFileSync(notesPath, `${plan.notes}\n\nArchevolt-Package-SHA256: ${packages[0].digest}\nArchevolt-Symbols-SHA256: ${packages[1].digest}\n`);
    git("-c", "user.name=github-actions[bot]", "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com", "tag", "--cleanup=verbatim", "-a", tag, plan.sourceSha, "-F", notesPath);
  }
  git("push", "origin", `refs/tags/${tag}:refs/tags/${tag}`);
  if (!release) {
    release = await github(`repos/${repository}/releases`, { method: "POST",
      body: { tag_name: tag, target_commitish: plan.sourceSha, name: tag, body: plan.notes, draft: true, prerelease: false } });
  }
  for (const entry of packages) {
    if (release.assets.some((asset) => asset.name === entry.name)) continue;
    const upload = new URL(release.upload_url.replace(/\{.*$/u, ""));
    upload.searchParams.set("name", entry.name);
    await github(upload.href, { method: "POST", body: readFileSync(entry.path) });
  }
  output({ publish: true, version: plan.version, source_sha: plan.sourceSha, tag });
}

async function publish() {
  const state = await releaseState();
  if (state.kind === "published") return;
  if (state.kind !== "reserved" || !state.release) {
    throw new Error("Prepare and stage the release before publishing it.");
  }
  mkdirSync(releaseDirectory, { recursive: true });
  const { plan, release } = state;
  const packages = releasePackages(state);
  for (const entry of packages) {
    entry.path = await downloadAsset(release, plan, entry);
    if (!entry.path) throw new Error(`Draft v${plan.version} has no staged ${entry.name}. Rerun prepare.`);
  }
  const path = packages[0].path;
  const apiKey = process.env.NUGET_API_KEY;
  if (!apiKey) throw new Error("NUGET_API_KEY is missing. Run NuGet/login before publishing.");
  let pushFailed = false;
  try {
    command("dotnet", ["nuget", "push", path, "--source", "https://api.nuget.org/v3/index.json", "--api-key", apiKey, "--skip-duplicate", "--no-symbols"], { stdio: "inherit" });
  } catch {
    pushFailed = true;
  }
  const remote = join(releaseDirectory, `public-Archevolt.${plan.version}.nupkg`);
  const url = `https://api.nuget.org/v3-flatcontainer/archevolt/${plan.version}/archevolt.${plan.version}.nupkg`;
  const confirmationWindowMs = 60 * 60 * 1000;
  const confirmationIntervalMs = 15_000;
  const confirmationAttempts = 1 + confirmationWindowMs / confirmationIntervalMs;
  let confirmed = false;
  for (let attempt = 0; attempt < confirmationAttempts; attempt++) {
    const response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
    if (response.ok) {
      writeFileSync(remote, Buffer.from(await response.arrayBuffer()));
      verifyPackage(path, plan, remote, state.packageDigest);
      confirmed = true;
      break;
    }
    if (response.status !== 404) throw new Error(`NuGet package confirmation returned HTTP ${response.status}. The release remains draft.`);
    if (attempt + 1 < confirmationAttempts) await new Promise((resume) => setTimeout(resume, confirmationIntervalMs));
  }
  if (!confirmed) throw new Error(`NuGet did not expose ${url} after ${confirmationAttempts} attempts. The release remains draft. Rerun this workflow.${pushFailed ? " The push command also failed." : ""}`);
  for (const entry of packages.filter((item) => item.extension === "snupkg")) {
    try {
      command("dotnet", ["nuget", "push", entry.path, "--source", "https://api.nuget.org/v3/index.json", "--api-key", apiKey, "--skip-duplicate"], { stdio: "inherit" });
    } catch {
      throw new Error(`NuGet symbol upload failed for ${entry.name}. The release remains draft. Rerun this workflow.`);
    }
  }
  await github(`repos/${repository}/releases/${release.id}`, { method: "PATCH", body: {
    tag_name: state.tag, target_commitish: plan.sourceSha, name: state.tag,
    body: plan.notes, prerelease: false, draft: false, make_latest: "true",
  } });
  console.log(`Published v${plan.version} from ${plan.sourceSha}.`);
}

async function docs() {
  const hook = process.env.DOCS_DEPLOY_HOOK_URL;
  if (!hook) throw new Error("Set the DOCS_DEPLOY_HOOK_URL Actions secret to the Cloudflare deploy hook, then rerun this workflow.");
  const url = new URL(hook);
  if (url.protocol !== "https:" || url.hostname !== "api.cloudflare.com") {
    throw new Error("DOCS_DEPLOY_HOOK_URL must be an HTTPS Cloudflare deploy hook.");
  }
  const response = await fetch(url, { method: "POST", signal: AbortSignal.timeout(30_000) });
  if (!response.ok) throw new Error(`Cloudflare rejected the docs build with HTTP ${response.status}. Rerun this workflow to retry the hook.`);
  if (response.headers.get("content-type")?.includes("application/json") && (await response.json()).success === false) {
    throw new Error("Cloudflare rejected the docs build. Check the deploy hook configuration and rerun this workflow.");
  }
  console.log("Cloudflare accepted the documentation build request.");
}

try {
  const action = process.argv[2];
  if (action === "plan") console.log(JSON.stringify(await releaseState(), null, 2));
  else if (action === "prepare") await prepare();
  else if (action === "publish") await publish();
  else if (action === "docs") await docs();
  else throw new Error("Usage: node scripts/release.mjs <plan|prepare|publish|docs>");
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
