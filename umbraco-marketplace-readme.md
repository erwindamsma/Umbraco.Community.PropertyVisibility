# Property Visibility

[![NuGet](https://img.shields.io/nuget/vpre/Umbraco.Community.PropertyVisibility?color=0273B3)](https://www.nuget.org/packages/Umbraco.Community.PropertyVisibility)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-8AB803)](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/LICENSE)

Hide properties, groups and tabs per site in the Umbraco backoffice.

When several sites in one Umbraco installation share a document type, some of its fields often matter on one site only. Property Visibility hides those fields from editors, per site, without a second document type.

## Features

- Hide properties, tabs (with everything in them) and groups by alias, per document type or element type.
- Rules for every site, or for one site, identified by its root node's key, its name, or as the default site.
- Works inside blocks: Block List, Block Grid, Single Block and rich text editor blocks, for the content and the settings of a block.
- A rule keyed by a composition or a parent document type reaches every type composed of it.
- Rules in appsettings or in a separate JSON file, reloaded without a restart, with JSON schemas for IntelliSense.
- A health check with stable issue codes names every root node, site or alias that does not resolve, and every hidden mandatory property.
- Hidden values are kept through save, publish and reload.

## Quick start

```
dotnet add package Umbraco.Community.PropertyVisibility --prerelease
```

(`--prerelease` is needed until 1.0.0 ships.) Add this section to `appsettings.json`, next to the existing `Umbraco` section, replace the example aliases with your own, save, and reopen a document:

```json
{
  "PropertyVisibility": {
    "ContentTypes": {
      "landingPage": { "Containers": ["seoTab"], "Properties": ["relatedLinks"] },
      "promoBanner": { "Properties": ["overlayColour"] }
    }
  }
}
```

Keys are content type aliases, `Properties` are property aliases, `Containers` are tab and group aliases (`tab`, `tab/group` or a group outside a tab). The backoffice does not show tab and group aliases: Umbraco derives them from the name in camelCase ("Seo tab" becomes `seoTab`), and the tab's URL segment (`seo-tab`) is not the alias. An alias that does not exist hides nothing; the health check reports it (`PV104` for a content type) and, for a tab or group, lists the type's `tab` and `tab/group` aliases (`PV202`).

Requires Umbraco 17.6.2 or a later 17.x. For a trial site, use the 17.x templates (`dotnet new install Umbraco.Templates::17.7.0`): on an Umbraco 18 site the install reports `NU1107`, which should not be worked around.

## Configuration

Per-site rules, the rules file, matching order, blocks, every option and every issue code: [configuration reference](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/docs/configuration.md). Supported versions: [compatibility](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/docs/compatibility.md).

## What it does not do

- It is not authorization: hidden values are still saved, readable and writable through the Management API, returned by the Delivery API and rendered on the website. Use Umbraco's user group permissions to restrict access.
- Hidden mandatory properties still block publishing, without a hint on the hidden field (the health check warns).
- No rules per user group, per culture, or for media and member types; blocks outside documents get nothing hidden.
- Umbraco 17 only for now; Umbraco 18 support is planned as 2.0.

The complete list is in the [README](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility#what-it-does-not-do).

## Support

Report bugs and ask questions in [GitHub issues](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/issues). The bug template asks for the Umbraco and package versions, the configuration, the health check output and the browser console with the debug flag on. Report security issues privately, as described in [SECURITY.md](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/SECURITY.md).

Property Visibility is a community package. It is not made, endorsed or supported by Umbraco A/S. Umbraco is a trademark of Umbraco A/S.
