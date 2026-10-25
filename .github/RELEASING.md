# Release setup and recovery

Pushes to `main` run `.github/workflows/release.yml`. The first package is `Archevolt` 0.1.0. Later releases inspect every commit since the last published release. A breaking `!` or `BREAKING CHANGE:` footer selects a major version, including before 1.0. A `feat` selects a minor version. Every other nonempty range selects a patch version. Direct commits that do not follow Conventional Commits still appear in the notes.

The workflow runs all nine verification gates on Windows, including clean package consumers under JIT and NativeAOT. It then reserves an annotated `vX.Y.Z` tag at the tested commit and uploads both the `.nupkg` and Source Link `.snupkg` to a draft GitHub release. It publishes the release only after the public NuGet package matches the staged package and NuGet accepts the separate symbol upload. Symbol indexing can finish later. Blume reads these published release notes as the changelog.

## Configure NuGet trusted publishing

On NuGet.org, sign in as `Stephanvs` and create a GitHub Actions trusted publishing policy with these values:

| Field | Value |
| --- | --- |
| Repository owner | `Stephanvs` |
| Repository | `archevolt` |
| Workflow file | `release.yml` |
| Environment | Leave empty |
| Package pattern | `Archevolt` |

The policy must permit creation of the first `Archevolt` package and publication of later versions. The release job requests an OIDC token through `NuGet/login`. It does not use a stored NuGet API key. `NUGET_USER` is an optional Actions variable whose default is `Stephanvs`. Keep the workflow filename and empty environment consistent with the policy. See [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

## Configure the Cloudflare rebuild

Use the existing Cloudflare project that serves `https://archevolt.dev`. Set its production branch to `main` and build command to `pnpm run build`. For Pages, the build output directory is `dist`. Keep the existing Workers deploy command if the site uses Workers Builds.

The checked-in `wrangler.jsonc` configures the existing `archevolt` Worker to serve `dist`, including Blume's custom `404.html`, and includes the required Preview configuration. The current `npx wrangler deploy` and `npx wrangler preview` commands read this file without extra configuration flags. See [Workers Preview configuration](https://developers.cloudflare.com/workers/previews/configuration/#wrangler-configuration-file).

Set these build variables in Cloudflare's **Settings > Builds > Variables and secrets** for both **Production** and **Previews Base**:

| Variable | Value |
| --- | --- |
| `PNPM_VERSION` | `12.5.1` |
| `NODE_VERSION` | `24` |

Cloudflare uses these variables to override its installed tools before dependency installation. The repository also declares `pnpm@12.5.1` in `package.json` and Node 24 in `.node-version`. pnpm 12 is required for the workspace's `allowBuilds` configuration. See [Cloudflare Workers build image and version overrides](https://developers.cloudflare.com/workers/ci-cd/builds/build-image/#overriding-default-versions).

Create a deploy hook for `main` named `Archevolt releases`:

- **Pages:** Open **Workers & Pages**, select the Pages project, then **Settings > Builds > Add deploy hook**. Choose `main` and copy the generated URL. See [Pages deploy hooks](https://developers.cloudflare.com/pages/configuration/deploy-hooks/).
- **Workers Builds:** Open **Workers & Pages**, select the Git-connected Worker, then **Settings > Builds > Deploy Hooks**. Enter the name, choose `main`, select **Create**, and copy the URL. See [Workers deploy hooks](https://developers.cloudflare.com/workers/ci-cd/builds/deploy-hooks/).

In GitHub, open **Settings > Secrets and variables > Actions**. Add a repository secret named `DOCS_DEPLOY_HOOK_URL` containing that URL. The release workflow calls it after publication. The separate documentation workflow also calls it after a human publishes, edits, or deletes a release, and supports manual runs.

Configure `GITHUB_TOKEN` as a secret in both Cloudflare build environments, **Production** and **Previews Base**. Use a persistent fine-grained GitHub token restricted to `Stephanvs/archevolt` with **Contents: Read-only**. The temporary Actions token cannot authenticate a later Cloudflare build.

Public release fetching can work without authentication, but Cloudflare builds should authenticate to avoid depending on anonymous GitHub API access from shared hosts.

The `build` script preserves output in `artifacts/logs/docs-build.txt` and fails if Blume reports an unavailable source or an offline cache. Cloudflare must run this script so a failed release fetch cannot deploy a stale or empty changelog. An accepted hook means a build was requested; check the Cloudflare build log for deployment completion.

## Recover a failed release

Rerun the original failed **Release** run in GitHub Actions. Recovery keeps its source commit and version.

The driver reads a fresh GitHub release list up to three times when known version tags are missing, waiting one and then two seconds. It logs missing tags and read exhaustion. Reservation checks still apply to the final snapshot, so an older unfinished release continues to block later versions.

After NuGet accepts the upload, validation and indexing can take up to an hour. The workflow checks for the public package every 15 seconds through that validation window. If it is still unavailable, the workflow fails and keeps the GitHub release in draft. Wait for NuGet validation to finish, then rerun the original release run; it reuses the reserved version and staged package.

| Last completed operation | Retry behavior |
| --- | --- |
| Verification | Verifies again if either package asset is missing, checking rebuilt files against the reserved digests. |
| Tag reservation | Reuses the tag and its notes, then creates or reuses the draft. |
| Package upload to draft | Downloads and reuses both original package files, uploading only missing assets. |
| NuGet push | Confirms the public package, ignoring only NuGet's repository signature when comparing ZIP entries, then pushes symbols separately with duplicate handling. A failed symbol upload leaves the release in draft. |
| GitHub release publication | Skips all package operations and retries the Cloudflare hook. |

A reserved release at another commit blocks later package versions. Rerun the original run first. The tag's `Archevolt-Package-SHA256` and `Archevolt-Symbols-SHA256` trailers bind each verified package's sorted entry names and content hashes, excluding the NuGet repository signature. Recovery checks these digests before reusing assets or accepting rebuilt packages. ZIP timestamps and compression do not affect the digests. Older reservations without a symbols trailer retain their original package-only recovery behavior.

Do not move or delete its tag, replace its package asset, or publish the draft manually. A replaced draft asset or a different package already present on NuGet fails confirmation and leaves the GitHub release draft for investigation. Before publication, the driver restores the draft's canonical tag, source commit, title, notes, and stable-release status.

If only the site needs rebuilding, run **Rebuild release documentation** manually on `main`. Changing release notes in GitHub also requests a rebuild. Blume shows the latest 100 published stable releases by default; increase the source's `limit` in `blume.config.ts` if that history window becomes too short.

GitHub queues up to 100 pending release runs and does not guarantee push order. If a newer commit publishes first, an older covered run skips its package. Every commit remains in the next applicable release range, but there is no promise of one package per push.

## Run checks locally

```powershell
pnpm install --frozen-lockfile
pnpm run test:release
./scripts/verify.ps1 -PackageVersion 0.2.0-local -SourceRevisionId (git rev-parse HEAD)
pnpm run build
```

The release tests use temporary Git repositories and local HTTP/package fixtures. They do not publish packages or releases. `node scripts/release.mjs plan` reads the current repository and GitHub release state without changing it; set `GH_TOKEN` or `GITHUB_TOKEN` when needed. Use GitHub Actions for actual publication.
