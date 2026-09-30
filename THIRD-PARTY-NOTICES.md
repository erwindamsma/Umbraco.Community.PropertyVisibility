# Third-party notices

Property Visibility (Umbraco.Community.PropertyVisibility) is licensed under the MIT licence in `LICENSE`. Parts of this repository and of the NuGet package come from the third-party projects below, under their own licences. Each section names the files it covers.

## Hey API openapi-ts (in the NuGet package)

The backoffice client's HTTP client is generated from the client templates of [@hey-api/openapi-ts](https://github.com/hey-api/openapi-ts) (version 0.99.0). These files in the repository are copies of those templates:

- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/auth.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/bodySerializer.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/params.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/pathSerializer.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/queryKeySerializer.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/serverSentEvents.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/types.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/core/utils.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/client/client.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/client/index.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/client/types.gen.ts`
- `src/Umbraco.Community.PropertyVisibility/Client/src/api/client/utils.gen.ts`

In the NuGet package, the compiled form of this code is in `staticwebassets/App_Plugins/UmbracoCommunityPropertyVisibility/client.gen-<hash>.js`, which a site serves under `/App_Plugins/UmbracoCommunityPropertyVisibility/`. That file starts with a comment that carries the notice below. The other generated files in `Client/src/api` (`client.gen.ts`, `index.ts`, `sdk.gen.ts` and `types.gen.ts`) are generated from this package's own API description and are covered by `LICENSE`.

```text
MIT License

Copyright (c) Hey API

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Umbraco CMS (test site only, not in the NuGet package)

The test site's scaffolding comes from the Umbraco project template of [Umbraco CMS](https://github.com/umbraco/Umbraco-CMS):

- `src/Umbraco.Community.PropertyVisibility.TestSite/Program.cs`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/_ViewImports.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/Partials/blockgrid/area.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/Partials/blockgrid/areas.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/Partials/blockgrid/default.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/Partials/blockgrid/items.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/Views/Partials/blocklist/default.cshtml`
- `src/Umbraco.Community.PropertyVisibility.TestSite/wwwroot/favicon.ico`

```text
The MIT License (MIT)

Copyright (c) 2005-present Umbraco

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Acknowledgement: Opinionated Package Starter

The repository was first scaffolded with [Opinionated Package Starter](https://github.com/LottePitcher/opinionated-package-starter) by Lotte Pitcher (MIT licence, Copyright (c) 2023-2026 Lotte Pitcher). Its files have since been rewritten, and none of it is in the NuGet package; this is a courtesy acknowledgement.
