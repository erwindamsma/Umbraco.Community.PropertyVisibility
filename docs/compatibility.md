# Compatibility

This page records what has been checked, not what is expected to work.

The package depends on `Umbraco.Cms.Web.Common`, `Umbraco.Cms.Api.Common` and `Umbraco.Cms.Api.Management` in the range `[17.6.2, 18.0.0)`, on .NET 10.

| Umbraco | Status | Checked | How |
|---|---|---|---|
| 17.6.2 | Verified (dependency floor) | 2026-09-28 | Unit tests, the JSON schema check and the whole Playwright acceptance suite (`tests/Umbraco.Community.PropertyVisibility.AcceptanceTests`) against the SQLite test site on a clean database (uSync first-boot import), run locally on Windows and on Linux (WSL); the manual cases in `docs/testing.md` (run 2026-09-27) |
| 17.7.0 | Verified | 2026-09-29 | The same checks with the package, the test site and the unit tests built with `-p:UmbracoCmsVersion=[17.7.0]` (every `Umbraco.Cms` package at 17.7.0), run locally on Windows (2026-09-28) and on Linux by the first `compat.yml` run on GitHub ([run 36626416072](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/actions/runs/36626416072), 2026-09-29: build, client type check, unit tests, schema check and the whole Playwright suite, with only the opt-in measurement spec skipped); the manual cases in `docs/testing.md` on that build (run 2026-09-28); the client type-checked against `@umbraco-cms/backoffice` 17.7.0 (`build/ci/client-typecheck.sh`); the backoffice sources the client relies on compared with 17.6.2 (below) |
| 17.0 to 17.6.1 | Not supported | | Below the dependency floor: the backoffice APIs the client relies on (`configureClient` on the auth context, the property view guard, cache-busted package URLs) are verified on 17.6.2 and 17.7.0 only |
| 18.x | Not supported | | Planned as a 2.0 release |

Before the first release, 17.6.2 was verified on a local machine on Windows and on Linux (WSL), and 17.7.0 on Windows and, through `compat.yml`, on Linux on GitHub. On GitHub, `ci.yml` runs the automated checks against 17.6.2 on every pull request and push to `main`, and `compat.yml` repeats them every week against the latest 17.x (see [Recurring check](#recurring-check)).

Tabs and groups are hidden with a backoffice method (`removeContainer` on the content type structure) that is public but not documented as an extension point. Each new Umbraco minor is re-checked before it is marked verified here.

## Not tested

The package is designed for these cases, but no check has run them yet:

- SQL Server: every run used SQLite.
- Load balancing: the configuration analysis is designed to run on every server.
- macOS.
- Docker bind mounts, volumes and network shares with `DOTNET_USE_POLLING_FILE_WATCHER=1`: polling itself was checked, but not on such a mount.
- An Umbraco version without `removeContainer`: properties stay hidden and the backoffice shows one warning, by design. The warning also appears when `removeContainer` throws a `TypeError` (a changed signature); any other error skips that tab or group, is logged with the [debug flag](configuration.md#debugging-in-the-browser) and is tried again on the next structure change.
- The startup warning on an Umbraco major other than 17.

## What was re-checked for 17.7.0

- Content type structure: `removeContainer(contentTypeUnique, containerId, { preventRemovingProperties })`, `whenLoaded()` and `contentTypes` are unchanged, and there is still no guard for tabs or groups that could replace `removeContainer`.
- Document workspace: `resetState()` (clears the structure and the property view guard), `_processIncomingData`, `createScaffold` and the deprecated `getParentUnique` are unchanged. Languages now come from the app language context; this does not touch the package.
- Blocks: the block element manager is unchanged. The block workspace stops reading a structure that was destroyed while it loaded, inline Block List and Single Block blocks show a loader until their block workspace exists, and the inline block view drops the view of a tab that is no longer in the structure. None of this needs a package change.
- Auth context: `configureClient` and `getLatestToken` are unchanged. `getServerUrl()`, `isSessionValid()`, `isInitialized`, `setInitialized()` and `getAuthProviders()` now log deprecation warnings; the package uses none of them.
- Validation hints: the "Could not find the declared container" error the package marks as handled for the tabs and groups it removed has the same text.
- HTTP client: Umbraco 17.7.0 generates its own API client with a newer `@hey-api/openapi-ts`, and `@umbraco-cms/backoffice` 17.7.0 declares `@hey-api/openapi-ts` `>=0.99.0` as a peer dependency. The package's generated client only takes `auth`, `credentials`, `throwOnError` and the base URL from the backoffice client, so it compiles against the 17.6.2 and the 17.7.0 typings. Raising the client's `@umbraco-cms/backoffice` dev dependency to 17.7 needs `@hey-api/openapi-ts` 0.99 or later in the same change.
- Known upstream error: the inline Block List error listed in `docs/testing.md` (`KNOWN_UPSTREAM_ERRORS`, an unhandled rejection of `UmbBlockWorkspaceContext.load()` when an inline block is re-rendered while it loads) is not fixed in 17.7.0: runs on 17.7.0 logged it in specs `05`, `10` and `11`.
- Upstream issues, checked 2026-09-27: [#21253](https://github.com/umbraco/Umbraco-CMS/issues/21253) and [#21254](https://github.com/umbraco/Umbraco-CMS/issues/21254) are open, last updated 2026-01-07; discussions [#16329](https://github.com/umbraco/Umbraco-CMS/discussions/16329) (last activity 2026-02-13) and [#20538](https://github.com/umbraco/Umbraco-CMS/discussions/20538) (last activity 2025-11-07) have no new activity. No feature request for a container guard has been filed yet.

## Recurring check

Each new Umbraco 17.x minor is re-checked before its row says Verified: the backoffice sources the client relies on are compared with the last verified version, the package is built with `-p:UmbracoCmsVersion=[<version>]`, the unit tests, the schema check, the client type check against that version's typings, the Playwright suite and the manual cases in `docs/testing.md` are run, and the upstream issues above are checked. `compat.yml` repeats the automated part every Monday. GitHub disables a scheduled workflow after 60 days without repository activity, so check the date of the last green compatibility run before a release and re-enable the workflow in the Actions tab when needed.
