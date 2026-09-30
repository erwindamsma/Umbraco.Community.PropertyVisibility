# Property Visibility

Hide properties, groups and tabs per site in the Umbraco backoffice.

![One document type on two sites: the Seo tab, the banner image and the related links only where they are needed](https://raw.githubusercontent.com/erwindamsma/Umbraco.Community.PropertyVisibility/main/docs/screenshots/before-after.png)

When several sites in one Umbraco installation share a document type, some of its fields often matter on one site only. This package hides those properties, tabs and groups per site, in documents and inside blocks, from rules in appsettings or a JSON file that reload without a restart. A health check reports every rule that does not resolve.

It changes what editors see, not what they are allowed to do: hidden values are still saved with the document, readable and writable through the Management API, and returned by the Delivery API.

## Install

```
dotnet add package Umbraco.Community.PropertyVisibility
```

Umbraco 17.6.2 or a later 17.x, .NET 10. No startup code is needed. For a trial site, use the 17.x templates (`dotnet new install Umbraco.Templates::17.7.0`): on an Umbraco 18 site the install reports `NU1107`, which should not be worked around.

## Single site

Add this section to `appsettings.json`, next to the existing `Umbraco` section, and replace the example aliases with your own:

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

Save the file and reopen a document: on every `landingPage` the `seoTab` tab and the `relatedLinks` property are hidden, and in every `promoBanner` block the `overlayColour` property.

The backoffice does not show tab and group aliases: Umbraco derives them from the name in camelCase ("Seo tab" becomes `seoTab`), and the tab's URL segment (`seo-tab`) is not the alias. An alias that does not exist hides nothing; the health check reports it (`PV104` for a content type) and, for a tab or group, lists the type's `tab` and `tab/group` aliases (`PV202`).

## Documentation

Per-site rules, the rules file, blocks, the health check and what the package does not do: [README on GitHub](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility#readme) and the [configuration reference](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/docs/configuration.md).

Property Visibility is a community package. It is not made, endorsed or supported by Umbraco A/S. Umbraco is a trademark of Umbraco A/S.
