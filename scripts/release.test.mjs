import assert from "node:assert/strict";
import { execFile, execFileSync } from "node:child_process";
import { createServer } from "node:http";
import { copyFile, mkdtemp, mkdir, readFile, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import test from "node:test";

const execute = promisify(execFile);
const releaseScript = fileURLToPath(new URL("./release.mjs", import.meta.url));
const packageScript = fileURLToPath(new URL("./verify-package.ps1", import.meta.url));

async function fixture(t, { isolatedScripts = false, expireConnections = false } = {}) {
  const directory = await mkdtemp(join(tmpdir(), "archevolt-release-"));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const git = (...args) => execFileSync("git", args, { cwd: directory, encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }).trim();
  git("init", "--initial-branch=main");
  git("config", "user.name", "Release test");
  git("config", "user.email", "release@example.invalid");
  await writeFile(join(directory, ".git/info/exclude"), "*\n");
  const state = { releases: [], assets: new Map(), writes: [], failUploadResponse: false };
  let releaseSocket;
  const server = createServer(async (request, response) => {
    const url = new URL(request.url, "http://localhost");
    if (url.pathname === "/expire") {
      if (releaseSocket && !releaseSocket.destroyed) releaseSocket.resetAndDestroy();
      response.end();
      return;
    }
    if (request.method !== "GET") state.writes.push({ method: request.method, path: url.pathname });
    const asset = /\/releases\/assets\/(\d+)$/u.exec(url.pathname);
    if (asset) {
      response.end(state.assets.get(Number(asset[1])));
      return;
    }
    if (url.pathname === "/nuget/package") {
      response.end(state.nugetPackage);
      return;
    }
    if (request.method === "PATCH" && url.pathname === "/repos/fixture/archevolt/releases/2") {
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      const release = state.releases.find((entry) => entry.id === 2);
      Object.assign(release, JSON.parse(Buffer.concat(chunks)));
      response.setHeader("content-type", "application/json");
      response.end(JSON.stringify(release));
      return;
    }
    if (url.pathname === "/repos/fixture/archevolt/releases") {
      if (request.method === "POST") {
        const chunks = [];
        for await (const chunk of request) chunks.push(chunk);
        const release = { ...JSON.parse(Buffer.concat(chunks)), id: 2, assets: [], upload_url: `${apiUrl}uploads/2/assets{?name,label}` };
        state.releases.push(release);
        response.setHeader("content-type", "application/json");
        response.end(JSON.stringify(release));
        return;
      }
      if (expireConnections) {
        response.setHeader("Connection", "keep-alive");
        response.setHeader("Keep-Alive", "timeout=60");
        releaseSocket = request.socket;
      }
      response.setHeader("content-type", "application/json");
      response.end(JSON.stringify(state.releases));
      return;
    }
    if (url.pathname === "/uploads/2/assets") {
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      const asset = { id: 7 + state.assets.size, name: url.searchParams.get("name") };
      state.assets.set(asset.id, Buffer.concat(chunks));
      state.releases.find((release) => release.id === 2).assets.push(asset);
      response.setHeader("content-type", "application/json");
      response.statusCode = state.failUploadResponse ? 500 : 201;
      state.failUploadResponse = false;
      response.end(JSON.stringify(asset));
      return;
    }
    response.statusCode = 404;
    response.end();
  });
  await new Promise((ready) => server.listen(0, "127.0.0.1", ready));
  t.after(() => new Promise((done) => server.close(done)));
  const apiUrl = `http://127.0.0.1:${server.address().port}/`;
  const env = {
    ...process.env, GITHUB_REPOSITORY: "fixture/archevolt",
    GITHUB_API_URL: apiUrl,
    GITHUB_OUTPUT: "", GH_TOKEN: "", GITHUB_TOKEN: "",
  };
  let script = releaseScript;
  if (isolatedScripts) {
    await mkdir(join(directory, "scripts"));
    script = join(directory, "scripts/release.mjs");
    await copyFile(releaseScript, script);
    await copyFile(packageScript, join(directory, "scripts/verify-package.ps1"));
    await symlink(fileURLToPath(new URL("../node_modules", import.meta.url)), join(directory, "node_modules"), "junction");
  }
  const cli = (action, extraEnv = {}) => execute(process.execPath, [script, action], { cwd: directory, env: { ...env, ...extraEnv } });
  const commit = (message) => {
    git("commit", "--allow-empty", "-m", message);
    return git("rev-parse", "HEAD");
  };
  const published = (tag = "v0.1.0") => {
    git("tag", "-a", tag, "-m", `Notes for ${tag}`);
    state.releases.push({ id: 1, tag_name: tag, draft: false, assets: [] });
  };
  return { directory, git, state, cli, commit, published };
}

test("the first release is 0.1.0 and includes ordinary direct commits", async (t) => {
  const repo = await fixture(t);
  const sha = repo.commit("Initial implementation");
  const { plan, kind } = JSON.parse((await repo.cli("plan")).stdout);
  assert.equal(kind, "new");
  assert.equal(plan.sourceSha, sha);
  assert.equal(plan.previousTag, null);
  assert.equal(plan.version, "0.1.0");
  assert.match(plan.notes, /Initial implementation/u);
  assert.match(plan.notes, new RegExp(`/commit/${sha}`, "u"));
});

test("the full commit range selects strict semantic versions below 1.0", async (t) => {
  for (const [messages, expected] of [
    [["fix: repair migration"], "0.1.1"],
    [["docs: explain queries"], "0.1.1"],
    [["Maintain examples"], "0.1.1"],
    [["feat: add bundles", "chore: update lockfile"], "0.2.0"],
    [["feat!: replace world API", "fix: update examples"], "1.0.0"],
    [["refactor: move world\n\nBREAKING CHANGE: remove the old constructor", "docs: update guide"], "1.0.0"],
  ]) {
    await t.test(messages[0], async (t) => {
      const repo = await fixture(t);
      repo.commit("Initial implementation");
      repo.published();
      const commits = messages.map((message) => repo.commit(message));
      const { plan } = JSON.parse((await repo.cli("plan")).stdout);
      assert.equal(plan.previousTag, "v0.1.0");
      assert.equal(plan.version, expected);
      for (const sha of commits) assert.ok(plan.notes.includes(`/commit/${sha}`), `notes include ${sha}`);
    });
  }
});

test("an already covered source skips package publication", async (t) => {
  const repo = await fixture(t);
  const oldSha = repo.commit("Initial implementation");
  repo.commit("feat: add bundles");
  repo.published();
  repo.git("checkout", "--detach", oldSha);
  const result = JSON.parse((await repo.cli("prepare")).stdout);
  assert.deepEqual(result, { publish: false, tag: "v0.1.0" });
  assert.deepEqual(repo.state.writes, []);
});

test("a reserved source reuses its version and notes and blocks later commits", async (t) => {
  const repo = await fixture(t);
  repo.commit("Initial implementation");
  repo.git("tag", "-a", "v0.1.0", "-m", `The tested release notes.\n\nArchevolt-Package-SHA256: ${"0".repeat(64)}`);
  const result = JSON.parse((await repo.cli("plan")).stdout);
  assert.equal(result.kind, "reserved");
  assert.equal(result.plan.version, "0.1.0");
  assert.equal(result.plan.notes, "The tested release notes.");
  repo.commit("fix: later correction");
  await assert.rejects(repo.cli("plan"), (error) => {
    assert.match(error.stderr, /Unfinished release v0\.1\.0/u);
    assert.match(error.stderr, /Rerun its original release\.yml/u);
    return true;
  });
  assert.deepEqual(repo.state.writes, []);
});

test("a source on a divergent branch cannot publish over a released history", async (t) => {
  const repo = await fixture(t);
  const root = repo.commit("Initial implementation");
  repo.commit("feat: published change");
  repo.published();
  repo.git("checkout", "--detach", root);
  repo.commit("fix: divergent change");
  await assert.rejects(repo.cli("plan"), (error) => /diverges from published v0\.1\.0/u.test(error.stderr));
});

test("uncommitted source files cannot be staged as a release", async (t) => {
  const repo = await fixture(t);
  await writeFile(join(repo.directory, "tracked.txt"), "committed");
  repo.git("add", "--force", "tracked.txt");
  repo.commit("Initial implementation");
  await writeFile(join(repo.directory, "tracked.txt"), "uncommitted");
  await assert.rejects(repo.cli("prepare"), (error) => /uncommitted files/u.test(error.stderr));
  assert.equal(repo.git("tag", "--list"), "");
  assert.deepEqual(repo.state.writes, []);
});

async function makePackage(directory, name, sha, { signature = false, payload = "assembly", version = "0.1.0", extension = "nupkg" } = {}) {
  const contents = join(directory, `${name}-contents`);
  await mkdir(contents);
  await writeFile(join(contents, "Archevolt.nuspec"), `<package><metadata><id>Archevolt</id><version>${version}</version><repository type="git" commit="${sha}" /></metadata></package>`);
  await writeFile(join(contents, extension === "snupkg" ? "Archevolt.pdb" : "Archevolt.dll"), payload);
  if (signature) await writeFile(join(contents, ".signature.p7s"), "NuGet repository signature");
  const script = join(directory, "zip.ps1");
  await writeFile(script, "param($Source, $Destination)\nAdd-Type -AssemblyName System.IO.Compression.ZipFile\n[System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $Destination)\n");
  const path = join(directory, `${name}.${extension}`);
  await execute("pwsh", ["-NoProfile", "-File", script, contents, path]);
  return path;
}

async function digestPackage(path, sha) {
  return (await execute("pwsh", ["-NoProfile", "-File", packageScript, "-Package", path, "-PackageVersion", "0.1.0", "-SourceRevisionId", sha, "-Fingerprint"])).stdout.trim();
}

test("a staged release retry downloads both original packages without replacing assets", async (t) => {
  const repo = await fixture(t);
  const sha = repo.commit("Initial implementation");
  const remote = join(repo.directory, "origin.git");
  repo.git("init", "--bare", remote);
  repo.git("remote", "add", "origin", remote);
  const original = await makePackage(repo.directory, "original", sha);
  const symbols = await makePackage(repo.directory, "symbols", sha, { extension: "snupkg" });
  const digest = await digestPackage(original, sha);
  const symbolsDigest = await digestPackage(symbols, sha);
  repo.git("tag", "-a", "v0.1.0", "-m", `The tested release notes.\n\nArchevolt-Package-SHA256: ${digest}\nArchevolt-Symbols-SHA256: ${symbolsDigest}`);
  repo.state.assets.set(7, await readFile(original));
  repo.state.assets.set(8, await readFile(symbols));
  repo.state.releases.push({ id: 2, tag_name: "v0.1.0", draft: true, assets: [
    { id: 7, name: "Archevolt.0.1.0.nupkg" }, { id: 8, name: "Archevolt.0.1.0.snupkg" },
  ] });
  const result = await repo.cli("prepare");
  assert.match(result.stdout, /"publish": true/u);
  assert.deepEqual(await readFile(join(repo.directory, "artifacts/release/Archevolt.0.1.0.nupkg")), await readFile(original));
  assert.deepEqual(await readFile(join(repo.directory, "artifacts/release/Archevolt.0.1.0.snupkg")), await readFile(symbols));
  assert.deepEqual(repo.state.writes, []);
  assert.equal(repo.git("--git-dir", remote, "rev-parse", "v0.1.0^{commit}"), sha);
});

test("a replaced draft asset is rejected even when version and commit metadata match", async (t) => {
  const repo = await fixture(t);
  const sha = repo.commit("Initial implementation");
  const original = await makePackage(repo.directory, "original", sha);
  const replacement = await makePackage(repo.directory, "replacement", sha, { payload: "unverified assembly" });
  const digest = await digestPackage(original, sha);
  repo.git("tag", "-a", "v0.1.0", "-m", `The tested release notes.\n\nArchevolt-Package-SHA256: ${digest}`);
  repo.state.assets.set(7, await readFile(replacement));
  repo.state.releases.push({ id: 2, tag_name: "v0.1.0", draft: true, assets: [{ id: 7, name: "Archevolt.0.1.0.nupkg" }] });
  await assert.rejects(repo.cli("prepare"), (error) => /digest does not match the reserved release tag/u.test(error.stderr));
  assert.deepEqual(repo.state.writes, []);
});

async function seedRelease(repo, expireConnection = false) {
  const sha = repo.commit("feat: initial implementation");
  const remote = join(repo.directory, "origin.git");
  repo.git("init", "--bare", remote);
  repo.git("remote", "add", "origin", remote);
  const original = await makePackage(repo.directory, "seed", sha);
  const symbols = await makePackage(repo.directory, "symbols", sha, { extension: "snupkg" });
  await writeFile(join(repo.directory, "scripts/verify.ps1"), `param($PackageVersion, $SourceRevisionId)
$ErrorActionPreference = 'Stop'
${expireConnection ? "Invoke-WebRequest -Uri ($env:GITHUB_API_URL + 'expire') | Out-Null\nStart-Sleep -Milliseconds 100" : ""}
Add-Content -LiteralPath verification-runs.txt -Value $PackageVersion
$null = New-Item -ItemType Directory -Force -Path artifacts/packages
Copy-Item -LiteralPath seed.nupkg -Destination "artifacts/packages/Archevolt.$PackageVersion.nupkg"
Copy-Item -LiteralPath symbols.snupkg -Destination "artifacts/packages/Archevolt.$PackageVersion.snupkg"
`);
  return { sha, remote, original, symbols };
}

test("a fresh release stages both packages after its idle GitHub connection expires during verification", async (t) => {
  const repo = await fixture(t, { isolatedScripts: true, expireConnections: true });
  const { sha, remote, original, symbols } = await seedRelease(repo, true);
  assert.match((await repo.cli("prepare")).stdout, /"publish": true/u);
  assert.equal(repo.git("--git-dir", remote, "rev-parse", "v0.1.0^{commit}"), sha);
  assert.equal(repo.state.releases.length, 1);
  assert.equal(repo.state.releases[0].draft, true);
  assert.deepEqual(repo.state.releases[0].assets.map((asset) => asset.name), [
    "Archevolt.0.1.0.nupkg", "Archevolt.0.1.0.snupkg",
  ]);
  assert.deepEqual(repo.state.assets.get(7), await readFile(original));
  assert.deepEqual(repo.state.assets.get(8), await readFile(symbols));
});

test("a lost upload response resumes the original draft and package after verification", async (t) => {
  const repo = await fixture(t, { isolatedScripts: true });
  const { sha, remote, original, symbols } = await seedRelease(repo);
  repo.state.failUploadResponse = true;
  await assert.rejects(repo.cli("prepare"), (error) => /GitHub POST \/uploads\/2\/assets returned HTTP 500/u.test(error.stderr));
  const reserved = repo.git("rev-parse", "v0.1.0^{commit}");
  assert.equal(reserved, sha);
  assert.equal(repo.git("--git-dir", remote, "rev-parse", "v0.1.0^{commit}"), sha);
  assert.equal(repo.state.releases.length, 1);
  assert.equal(repo.state.releases[0].draft, true);
  const originalNotes = repo.state.releases[0].body;
  const recoveredNotes = JSON.parse((await repo.cli("plan")).stdout).plan.notes;
  assert.match(originalNotes, /^#{2,3} /mu);
  assert.match(recoveredNotes, /^#{2,3} /mu);
  assert.equal(recoveredNotes, originalNotes);
  assert.doesNotMatch(recoveredNotes, /Archevolt-Package-SHA256/u);
  assert.deepEqual(repo.state.assets.get(7), await readFile(original));
  assert.match((await repo.cli("prepare")).stdout, /"publish": true/u);
  assert.deepEqual(repo.state.assets.get(8), await readFile(symbols));
  assert.match((await repo.cli("prepare")).stdout, /"publish": true/u);
  assert.deepEqual((await readFile(join(repo.directory, "verification-runs.txt"), "utf8")).trim().split(/\r?\n/u), ["0.1.0", "0.1.0"]);
  assert.deepEqual(repo.state.writes, [
    { method: "POST", path: "/repos/fixture/archevolt/releases" },
    { method: "POST", path: "/uploads/2/assets" },
    { method: "POST", path: "/uploads/2/assets" },
  ]);
});

test("NuGet duplicate confirmation permits a signature and rejects changed bytes or source metadata", async (t) => {
  const repo = await fixture(t);
  const sha = repo.commit("Initial implementation");
  const staged = await makePackage(repo.directory, "staged", sha);
  const signed = await makePackage(repo.directory, "signed", sha, { signature: true });
  const changed = await makePackage(repo.directory, "changed", sha, { payload: "different assembly" });
  const wrongSource = await makePackage(repo.directory, "wrong-source", "f".repeat(40));
  const compare = (path) => execute("pwsh", ["-NoProfile", "-File", packageScript, "-Package", staged, "-PackageVersion", "0.1.0", "-SourceRevisionId", sha, "-CompareWith", path]);
  assert.match((await compare(signed)).stdout, /matches the staged package/u);
  await assert.rejects(compare(changed), (error) => /differs from the staged package/u.test(error.stderr));
  await assert.rejects(compare(wrongSource), (error) => /does not match Archevolt/u.test(error.stderr));
});

test("publication retries both packages and keeps the release draft when the symbol upload fails", async (t) => {
  const repo = await fixture(t);
  const sha = repo.commit("fix: publish debug symbols");
  const packagePath = await makePackage(repo.directory, "original", sha);
  const symbolsPath = await makePackage(repo.directory, "symbols", sha, { extension: "snupkg" });
  const digest = await digestPackage(packagePath, sha);
  const symbolsDigest = await digestPackage(symbolsPath, sha);
  repo.git("tag", "-a", "v0.1.0", "-m", `Release notes.\n\nArchevolt-Package-SHA256: ${digest}\nArchevolt-Symbols-SHA256: ${symbolsDigest}`);
  repo.state.assets.set(7, await readFile(packagePath));
  repo.state.assets.set(8, await readFile(symbolsPath));
  repo.state.nugetPackage = await readFile(packagePath);
  repo.state.releases.push({ id: 2, tag_name: "v0.1.0", draft: true, assets: [
    { id: 7, name: "Archevolt.0.1.0.nupkg" }, { id: 8, name: "Archevolt.0.1.0.snupkg" },
  ] });
  await writeFile(join(repo.directory, "transport.mjs"), `
import childProcess from "node:child_process";
import { syncBuiltinESMExports } from "node:module";
import { appendFileSync, existsSync } from "node:fs";
import { basename } from "node:path";
const originalExec = childProcess.execFileSync;
childProcess.execFileSync = (file, args, options) => {
  if (file !== "dotnet") return originalExec(file, args, options);
  if (args[0] !== "nuget" || args[1] !== "push") throw new Error("Unexpected dotnet command");
  appendFileSync("pushes.txt", basename(args[2]) + "\\n");
  if (args[2].endsWith(".snupkg") && existsSync("fail-symbols")) throw new Error("Symbol server unavailable");
  return "";
};
syncBuiltinESMExports();
const originalFetch = globalThis.fetch;
globalThis.fetch = (url, options) => {
  if (String(url).startsWith("https://api.nuget.org/v3-flatcontainer/")) {
    return originalFetch(new URL("nuget/package", process.env.GITHUB_API_URL), options);
  }
  if (!String(url).startsWith(process.env.GITHUB_API_URL)) throw new Error("Unexpected external request");
  return originalFetch(url, options);
};
`);
  const env = { NODE_OPTIONS: "--import=./transport.mjs", NUGET_API_KEY: "fixture-key" };
  await writeFile(join(repo.directory, "fail-symbols"), "");
  await assert.rejects(repo.cli("publish", env), (error) => /NuGet symbol upload failed/u.test(error.stderr));
  assert.equal(repo.state.releases[0].draft, true);
  assert.deepEqual(repo.state.writes, []);
  await rm(join(repo.directory, "fail-symbols"));
  await repo.cli("publish", env);
  assert.equal(repo.state.releases[0].draft, false);
  assert.deepEqual((await readFile(join(repo.directory, "pushes.txt"), "utf8")).trim().split("\n"), [
    "Archevolt.0.1.0.nupkg", "Archevolt.0.1.0.snupkg",
    "Archevolt.0.1.0.nupkg", "Archevolt.0.1.0.snupkg",
  ]);
});
