# Contributing

Contributions are welcome: bug reports, documentation fixes, tests and code.

## Proposing a change

- For a bug, open an issue with the bug template first, unless the fix is small and obvious. For a new feature or a behaviour change, open an issue before writing code, so the approach can be agreed before you spend time on it. Check [What it does not do](README.md#what-it-does-not-do) first: some limits are deliberate.
- Fork the repository, branch from `main`, and keep one topic per pull request.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `ci:`, `chore:`).
- Fill in the pull request template. CI runs the build, the unit tests, the schema check, the package inspection and the whole Playwright suite on every pull request.

## Toolchain

- dotnet SDK 10.0.x (`global.json` rolls forward within the 10.0 feature band)
- Node 24 LTS (24.13 or later) and the npm it ships with (the client build runs `npm ci`, so keep `package-lock.json` committed). The workflows use Node 24. The `engines` of both `package.json` files declare `node >=24.13`, the range `@umbraco-cms/backoffice` 17.6.2 and `@umbraco-ui/uui` declare. npm does not enforce it: on an older Node, `npm ci` prints `EBADENGINE` warnings and carries on, so check `node -v` first.

## Building

- `dotnet build Umbraco.Community.PropertyVisibility.slnx` builds everything. The package project runs `npm ci` and `npm run build` in `src/Umbraco.Community.PropertyVisibility/Client` through the `BuildClient` MSBuild target; pass `-p:SkipClientBuild=true` when the client output is already up to date.
- `dotnet run --project src/Umbraco.Community.PropertyVisibility.TestSite --launch-profile https` starts the test site on https://localhost:44300. It installs unattended on SQLite and imports the uSync set in `uSync/v17` on first boot. Development login: `admin@example.com` / `PropertyVisibilityDev1!` (a documented dummy, Development environment only). `appsettings.Development.json` also sets a fixed dummy `Umbraco:CMS:Imaging:HMACSecretKey`, so the unattended install does not generate a random key and write it into the tracked `appsettings.json`. These two dummies are the only credentials allowed in the repository (see [docs/testing.md](docs/testing.md#publication-checklist-accepted-exceptions)); if a boot ever leaves `appsettings.json` modified, do not commit that change.
- `dotnet test Umbraco.Community.PropertyVisibility.slnx` runs the unit tests.
- Acceptance tests (Playwright, against the running test site): in `tests/Umbraco.Community.PropertyVisibility.AcceptanceTests` run `npm ci`, `npx playwright install chromium` once, then `npm run test:acceptance`. The suite changes the running site's `appsettings.json` and writes a temporary `PropertyVisibility.config.json` into its content root; both are restored in `finally` blocks, and by the global teardown (or the next run's setup) after an interrupted run. To run one spec, pass `--no-deps` so the console-errors spec (a teardown project that needs the whole run) is left out. See [docs/testing.md](docs/testing.md).

## Tests to run

| What changed | Run before opening the pull request |
|---|---|
| Anything | `dotnet test Umbraco.Community.PropertyVisibility.slnx` (unit tests, including the documentation checks below) |
| Server code | The unit tests, with new or updated tests in `tests/Umbraco.Community.PropertyVisibility.Tests` |
| Anything under `Client/` | `npm run typecheck` and `npm run build` in the client, then the whole acceptance suite against the test site on a clean database ([docs/testing.md](docs/testing.md)) |
| The hidden-fields API | Regenerate the API client (below), then the whole acceptance suite |
| The options model | Regenerate the JSON schemas (below); the schema check and the unit tests |
| Packaging | `bash build/ci/pack.sh` and `bash build/ci/test-inspect-nupkg.sh` |
| Documentation only | The unit tests (documentation checks) |

The documentation checks in the unit test project (`Documentation/` and `Configuration/IssueCodesDocumentationTests.cs`) fail when:

- the options table in [docs/configuration.md](docs/configuration.md#options) and the options model differ (names, types, defaults), or the documented rules file keys differ from the rules file model;
- the issue code table and `IssueCodes` differ, or a document or issue template mentions a code that does not exist;
- a relative link or image in a Markdown file points to a file, folder or heading that does not exist (paths are compared case-sensitively, as on GitHub);
- a link to this repository's `main` branch (`github.com/.../blob/main/...`, `tree/main`, `raw.githubusercontent.com/.../main/...`) in the docs, the issue templates, `umbraco-marketplace.json` or the code points to a file or heading that does not exist;
- `docs/README_nuget.md`, `umbraco-marketplace-readme.md` or `CHANGELOG.md` contain a relative link: NuGet, the Umbraco Marketplace and GitHub release notes cannot resolve one;
- a complete JSON sample in the READMEs or in `docs/configuration.md` does not validate against the package's JSON schemas;
- the tagline differs between the package `Description`, the READMEs and `umbraco-marketplace.json`.

The console-errors spec (`08`) must stay green. Do not silence a new console error through `KNOWN_UPSTREAM_ERRORS`: that list holds one Umbraco error that also occurs on a site without this package ([docs/testing.md](docs/testing.md)).

## Regenerating generated code

- API client: with the test site running, `npm run generate-client` in `src/Umbraco.Community.PropertyVisibility/Client` regenerates `src/api` from the package's Swagger document. Commit the result. The generator is `@hey-api/openapi-ts` 0.85.2, the lowest version the peer range of the `@umbraco-cms/backoffice` 17.6.2 dev dependency accepts. `npm audit` in the client reports advisories in dev dependencies only, and `npm audit --omit=dev` reports none: `@hey-api/openapi-ts` itself ([GHSA-hhx9-57xq-r5rw](https://github.com/advisories/GHSA-hhx9-57xq-r5rw), in its `buildClientParams` template), `handlebars` (used only while generating) and `dompurify` under `monaco-editor` (typings only). The `buildClientParams` template is committed in `src/api/core/params.gen.ts`, but nothing calls it, so the build leaves it out of the chunks; the package inspection fails when a chunk contains its `$body_` or `$query_` prefixes. Move to 0.99 or later together with the dev dependency on 17.7 (see [compatibility.md](docs/compatibility.md#what-was-re-checked-for-1770)).
- JSON schemas: after changing a property or an XML `<summary>` of `PropertyVisibilityOptions`, `SiteVisibilityOptions`, `ContentTypeVisibilityOptions` or `PropertyVisibilityConfigFile`, regenerate and commit both schemas: `dotnet run --project tools/Umbraco.Community.PropertyVisibility.SchemaGenerator -p:SkipClientBuild=true -- --out src/Umbraco.Community.PropertyVisibility`. CI runs the same tool with `--no-build ... --check` and fails with a line diff when the committed files drift.
- Package inspection: `bash tools/inspect-nupkg.sh <folder with nupkg> <version>` runs CI's package inspection locally (needs `unzip`; `jq` optional), for example after `dotnet pack src/Umbraco.Community.PropertyVisibility -c Release -p:Version=0.0.0-local.1 -o artifacts/local-pack`. It checks every nupkg in the folder against the version (nuspec and `umbraco-package.json`), so empty the folder before packing, and pack with `-p:Version`, not `-p:PackageVersion`. It also checks that the three Umbraco dependencies declare the `UmbracoCmsVersion` range of `src/Directory.Packages.props`, so pack after a restore with the default range (`bash build/ci/pack.sh` does all of this). The check is an allowlist: only the root files (nuspec, icon, NuGet readme, `LICENSE`, `THIRD-PARTY-NOTICES.md`, the two JSON schemas), the assembly and its XML documentation, the six props files and the client's JavaScript chunks and `umbraco-package.json` may ship; `LICENSE` and `THIRD-PARTY-NOTICES.md` must be there, and the one `client.gen-<hash>.js` chunk must carry the Hey API licence banner the client build prepends. No chunk may contain `$body_` or `$query_`, the prefixes of the Hey API `buildClientParams` code (see the API client above). A manual `dotnet pack` fails early when the client output is missing or its `umbraco-package.json` does not carry the `-p:Version` being packed.

## Run CI locally

The workflows (`ci.yml`, `compat.yml`, `release.yml`) only call the bash scripts in `build/ci`, so a local run executes the same commands. They run from any folder and need the toolchain above plus `curl`, `tar` and `unzip` (`test-inspect-nupkg.sh` also needs `python3` or `python`). Where they were run: every script on Linux (WSL Ubuntu, the way the workflows run them on `ubuntu-24.04`) and in Git Bash on Windows. On Windows, `start-test-site.sh` needs an existing ASP.NET Core development certificate (it creates one only on Linux; `dotnet dev-certs https` makes one), and `stop-test-site.sh` ends the site without a graceful shutdown, because Git Bash cannot send a Windows process a POSIX signal; start the test site with `dotnet run` instead (see [Building](#building)) when you need its shutdown logs. macOS is not tested.

| Script | What it does |
|---|---|
| `build.sh` | Client `npm ci`, `npm run typecheck` and `npm run build` with `PACKAGE_VERSION`, then `dotnet restore` and `dotnet build -c Release -p:SkipClientBuild=true`. Fails unless every `Umbraco.Cms*` package of the package, test site, unit test and schema generator projects (and the test site's `deps.json`) resolves to one version: `UMBRACO_CMS_VERSION` when set. |
| `test.sh` | Unit tests (`dotnet test --no-build`, results in `TestResults/`). |
| `schema-check.sh` | JSON schema drift check (`--check`). |
| `pack.sh` | `dotnet pack` into `artifacts/nupkg` (`PACK_OUTPUT`, a folder under `artifacts/`; only the `*.nupkg` and `*.snupkg` files in it are replaced) and `tools/inspect-nupkg.sh`; in a CI build also `check-deterministic-paths.cs` (every PDB path under `/_/`, no build machine path in the package). |
| `test-inspect-nupkg.sh` | Leak test for the inspection: a source map, `Client/package.json`, a TypeScript source, a `secrets.json` in the client output folder and one at the `staticwebassets` root, `content/appsettings.Production.json`, a missing `LICENSE`, a missing `THIRD-PARTY-NOTICES.md`, the Hey API banner stripped from the `client.gen` chunk, `$body_` added to the `client.gen` chunk and `$query_` to `property-visibility.js`, a wrong `umbraco-package.json` version and an Umbraco dependency pinned to one exact version must each fail it, the original must pass. |
| `client-typecheck.sh` | Type-checks a copy of the client against the `@umbraco-cms/backoffice` typings of `UMBRACO_CMS_VERSION` (compat.yml); the client's own `node_modules` and lockfile stay untouched. |
| `start-test-site.sh`, `stop-test-site.sh` | Start the built test site in the background on a clean SQLite database (it deletes `umbraco/Data`) and wait for `/umbraco` to answer 200; stop it again. PID file and console log in `artifacts/test-site`; a stale PID file whose process is not the test site is removed, never signalled. |
| `acceptance.sh` | `npm ci`, typecheck, `npx playwright install chromium` and the whole Playwright suite against `PV_BASE_URL`, bounded by `--global-timeout` (and `--max-failures` with `CI=true`); `playwright-report/` and `test-results/` stay in the acceptance project. |
| `resolve-umbraco-version.sh` | Prints the latest stable, listed Umbraco 17.x from nuget.org's registration index, or checks a given version (needs `node`). |

Environment: `PACKAGE_VERSION` (default `0.0.0-local`), `UMBRACO_CMS_VERSION` (build against one exact Umbraco version; the build fails when restore resolves another), `CI=true` (deterministic `ContinuousIntegrationBuild`, `playwright install --with-deps`, which uses sudo, and `--max-failures`), `PV_PORT` (test site https port, default 44300, http is the next port), `PV_GLOBAL_TIMEOUT_MS` (default 30 minutes) and `PV_MAX_FAILURES` (default 10) for `acceptance.sh`.

```bash
bash build/ci/build.sh && bash build/ci/test.sh && bash build/ci/schema-check.sh && bash build/ci/pack.sh && bash build/ci/test-inspect-nupkg.sh
bash build/ci/start-test-site.sh && bash build/ci/acceptance.sh; bash build/ci/stop-test-site.sh

# What compat.yml does, against the latest 17.x (the assignment first, so a failed lookup stops the chain)
UMBRACO_CMS_VERSION=$(bash build/ci/resolve-umbraco-version.sh) && export UMBRACO_CMS_VERSION \
  && bash build/ci/build.sh && bash build/ci/client-typecheck.sh && bash build/ci/test.sh && bash build/ci/schema-check.sh \
  && bash build/ci/start-test-site.sh && bash build/ci/acceptance.sh; bash build/ci/stop-test-site.sh
```

The test site started by `start-test-site.sh` runs the Release build output with `ASPNETCORE_ENVIRONMENT=Development`. It uses the checkout's test site folder as its content root, the same as `dotnet run`: the same `umbraco/Data` database folder, and the same `appsettings.json` that specs `10` to `12` rewrite. Stop a test site you started with `dotnet run` first; `start-test-site.sh` refuses to delete a database another process on the same machine holds open (Linux, with `fuser`), and on Windows the delete fails on the locked file. `PV_PORT` only moves the port: a second site next to a running one needs its own checkout. `pack.sh` refuses to run with `UMBRACO_CMS_VERSION` set, because the package would then depend on that exact version; build again without it first.

Scheduled runs: GitHub disables a scheduled workflow after 60 days without activity in a public repository, so `compat.yml` can stop running in a quiet period. Before a release, check the date of the last green compatibility run in the Actions tab and re-enable the workflow there when it was disabled.

## Keeping the docs in sync

The documentation is part of the change. What goes where:

| Change | Update |
|---|---|
| A new or changed option | The XML `<summary>` in the options model, the regenerated schemas, the [options table](docs/configuration.md#options) and, when users need it to get started, the README |
| A new issue code | `IssueCodes` (a new number; a released code never changes meaning and is never reused) and the [issue code table](docs/configuration.md#issue-codes) |
| A behaviour users can see | The README section it belongs to, `docs/configuration.md`, and an entry under `Unreleased` in `CHANGELOG.md` |
| A new limitation, or one that is lifted | [What it does not do](README.md#what-it-does-not-do) in the README, and the short list in `umbraco-marketplace-readme.md` |
| An Umbraco version verified | `Constants.TestedUmbracoVersions`, [docs/compatibility.md](docs/compatibility.md) and the version table in the README |
| An acceptance spec | The spec table in [docs/testing.md](docs/testing.md) |

`docs/README_nuget.md` (the NuGet readme), `umbraco-marketplace-readme.md` (the Marketplace listing) and `CHANGELOG.md` (the GitHub release notes) are shown outside this repository: use absolute URLs in them, and link images as `https://raw.githubusercontent.com/erwindamsma/Umbraco.Community.PropertyVisibility/main/...`. The README in the repository uses relative links. Screenshots live in `docs/screenshots` and are taken on the test site; keep their file names, because the Marketplace listing and the NuGet readme link to them. `docs/icon.png` is the package icon (128x128 PNG, packed into the nupkg). Its source is `docs/icon.svg`: after a change, render the SVG to a 128x128 PNG with a transparent background and without metadata chunks (for example a Chromium element screenshot with a transparent background), and check that the PNG holds only the `IHDR`, `IDAT` and `IEND` chunks.

## Keep samples neutral

This repository is public. Samples in code, docs, tests and screenshots use the names of the test site (`landingPage`, `article`, `siteSettings`, `promoBanner`, "Corporate site", "Campaign site", ...): no data from a real project, no internal URLs, no local machine paths or user names, and no real credentials or keys. Screenshots are taken on the test site only.

## Pull request checklist

The pull request template holds this checklist:

- `dotnet build` and `dotnet test` pass locally (the unit tests include the documentation checks).
- Tests added or updated for the change (unit tests, and acceptance specs for backoffice behaviour).
- Client type-checked and rebuilt (`npm run typecheck`, `npm run build`) when anything under `Client/` changed.
- Acceptance suite run against the test site on a clean database when the client or the API changed.
- API client regenerated (`npm run generate-client`) when the API changed.
- JSON schemas regenerated when the options model changed (see [Regenerating generated code](#regenerating-generated-code)).
- Package contents checked when packaging changed (`bash build/ci/pack.sh`).
- README and docs updated (options table, issue codes, "What it does not do", compatibility) where the change touches them (see [Keeping the docs in sync](#keeping-the-docs-in-sync)).
- `CHANGELOG.md` has an entry under `Unreleased`.
- Samples stay neutral (see [Keep samples neutral](#keep-samples-neutral)).

## Publishing (maintainers)

The order matters, because the package and its readmes link to the repository:

1. Before the repository becomes public, enable Private vulnerability reporting (Settings > Code security) and check that it is on: it is the only reporting channel [SECURITY.md](SECURITY.md) offers, and GitHub leaves it off by default.
2. In the release commit, rename `## [Unreleased]` in `CHANGELOG.md` to `## [<version>] - <date>` and add an empty `## [Unreleased]` above it: `release.yml` publishes that section as the release notes.
3. Make the repository public and push `main`, with `main` as the default branch. The NuGet readme, the Marketplace listing, the health check's documentation links and the compatibility link compiled into the package all point to files on `main`.
4. Only then push the tag `v<version>`: `release.yml` pushes the package to NuGet and creates the GitHub release.

## What logs and console output contain

- Server log: configuration issues once per configuration change, with configuration paths, root node keys and root node names. Like the health check, Umbraco's log viewer is available to every user with access to the Settings section, not only to administrators.
- Hidden-fields API: never a root node key or root node name, in `matchedSite` or in a warning. Any approved backoffice user with Content section access can call it for any document key: it does not check start nodes or document permissions, so the site label and the root resolution it returns must stay harmless.
- Browser console without the debug flag: only warnings (a failed hidden-fields request, once per page load; hiding tabs and groups unavailable; an unexpected apply or guard failure; a missing auth context). Each carries only the error's name, message and HTTP status, never the error object or the request URL, which can carry a parent key.
- Browser console with `localStorage['Umbraco.Community.PropertyVisibility.Debug'] = '1'`: the document key, content type key, how the parent was sent (never its key), root resolution, matched site label and reason, warnings and counts. The applied event carries `documentKey`, `contentTypeKey`, the counts and `target`. Nothing adds the key or name of the document's site root; when the open document is itself a root node, its own key is the document key. Keep it that way: log explicit summaries, never whole responses.
