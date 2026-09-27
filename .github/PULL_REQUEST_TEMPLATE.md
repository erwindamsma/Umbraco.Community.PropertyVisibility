## Summary

<!-- What changes and why. Link the issue it resolves, for example "Fixes #12". -->

## How it was tested

<!-- The commands you ran and on which Umbraco version, and the acceptance specs you added or changed. -->

## Checklist

- [ ] `dotnet build` and `dotnet test` pass locally (the unit tests include the documentation checks)
- [ ] Tests added or updated for the change (unit tests, and acceptance specs for backoffice behaviour)
- [ ] Client type-checked and rebuilt (`npm run typecheck`, `npm run build`) when anything under `Client/` changed
- [ ] Acceptance suite run against the test site on a clean database when the client or the API changed
- [ ] API client regenerated (`npm run generate-client`) when the API changed
- [ ] JSON schemas regenerated when the options model changed (`dotnet run --project tools/Umbraco.Community.PropertyVisibility.SchemaGenerator -p:SkipClientBuild=true -- --out src/Umbraco.Community.PropertyVisibility`)
- [ ] Package contents checked when packaging changed (`bash build/ci/pack.sh`)
- [ ] README and docs updated (options table, issue codes, "What it does not do", compatibility) where the change touches them
- [ ] `CHANGELOG.md` has an entry under `Unreleased`
- [ ] Samples stay neutral: the test site's names; no real project data, internal URLs, local paths or credentials
