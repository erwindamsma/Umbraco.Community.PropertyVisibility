# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).
The release workflow publishes the section whose heading matches the tag (`## [1.2.3]`) as the GitHub release notes.

## [Unreleased]

### Added

- Rule sets: rules that several sites share are written once under `RuleSets`, and each site names the sets it uses in `Include`. A site's rules are its own united with those of its rule sets, so a rule set can only add to what a site hides. A name in `Include` that matches no rule set is a configuration error (`PV009`: nothing is hidden until it is fixed), and a rule set that no site includes is a warning (`PV106`).
- `PV206` (informational): a content type entry without properties and containers, which hides nothing.
- The debug log's hidden-fields and applied lines carry their summary in the text of the line (site and match reason, counts, warning codes), so a copied console line keeps it.
- The README section "Moving from your own implementation", for sites that hid fields per site with code of their own, such as a `SendingContentNotification` handler before Umbraco 14.

### Changed

- `PV103` suggests a site with only a `RootNodeKey` for a root node that is not a site, and says that an `IsDefault` site also takes roots added later. The configuration reference describes this under "Roots that are not sites".

## [1.0.0-rc.2] - 2026-09-30

### Changed

- The backoffice API client is regenerated with `@hey-api/openapi-ts` 0.99.0 (was 0.85.2); behaviour is unchanged.

## [1.0.0-rc.1] - 2026-09-29

The first release, published as `1.0.0-rc.1`. It supports Umbraco 17.6.2 and later 17.x versions (verified on 17.6.2 and 17.7.0) on .NET 10.

### Added

- Hide properties, tabs (with every group and property in them) and groups in the backoffice, per document type or element type, by alias. Rules under the top-level `ContentTypes` apply on every site; rules under `Sites` apply to one site. A group in a tab is addressed as `tab/group`, split at the first `/` as Umbraco does, so groups whose name contains `/` work too.
- Site matching by the document's root node: by `RootNodeKey`, then by `RootNodeName` (case-insensitive), then the `IsDefault` site. A new document uses the root of the parent it is created under; a document created at the content root gets the default site until its first save; documents in the recycle bin get the top-level rules only.
- Rules keyed by a composition or a parent document type apply to every type composed of it, directly or transitively, and resolve against the composition's own properties, tabs and groups. Rules of a type and of all its compositions, top-level and per site, are united.
- `HideEmptiedContainers` (on by default): a group whose properties are all hidden, and a tab whose own properties and groups are all hidden, disappear too.
- Hiding inside blocks, with the site of the document that holds the block: the block's content follows its content element type, its settings its settings element type. Covered by the acceptance suite: Block List in a modal and inline, Block Grid in a modal, nested blocks, blocks in a document that is not saved yet, and split view on a culture-variant document. Checked by hand: Single Block, rich text editor blocks and blocks pasted from the clipboard. Each element type is requested once per document.
- Blocks in media items, members and document blueprints get nothing hidden, also when they are opened from inside a document.
- Hidden values are kept: tabs and groups are removed from the workspace without their properties, so hidden values survive save, publish and reload.
- Configuration in the `PropertyVisibility` appsettings section, or in the optional rules file `PropertyVisibility.config.json` (option `ConfigFile`), whose rules replace the appsettings rules when it is valid. Both reload without a restart. A broken edit of the rules file keeps the last valid version and is reported with its position or JSON path and a "Did you mean" suggestion, and a file that is not UTF-8 with the position of the first invalid byte; a locked file is read again automatically, and one that cannot be read for lack of access is reported and read again on its next change; for Docker volumes, network shares and a rules file that is a symbolic link, set `DOTNET_USE_POLLING_FILE_WATCHER`.
- `Enabled` kill switch.
- JSON schemas for the appsettings section and the rules file, wired into the site by the package's build props, so editors complete and check the configuration after the first build.
- The "Property Visibility configuration" health check (Settings > Health Check > Configuration): every root node, site, content type, property or container alias that does not resolve, hidden mandatory properties, the rules source, load time and rules hash, and the Umbraco version against the tested versions. Issue codes `PV001` to `PV008`, `PV101` to `PV105`, `PV201` to `PV205` and `PV301` to `PV304` never change meaning. The same issues are logged once per configuration change, and again when a root node is created, renamed, moved, trashed or deleted.
- The hidden-fields endpoint `GET /umbraco/property-visibility/v1/hiddenfields` (approved backoffice users with Content section access), with its own Swagger document. The response carries the property and container keys, the root resolution, the matched site's label and match reason, and issue-coded warnings; never a root node's key or name.
- A browser debug flag (`localStorage['Umbraco.Community.PropertyVisibility.Debug'] = '1'`) and the `umbraco-community-property-visibility:applied` window event after every completed pass, for tests and other packages.
- When a hidden mandatory property fails validation, Umbraco's "Could not find the declared container" error for the removed tab or group is handled, so it does not show up as an uncaught error; Umbraco still blocks the publish with its own notification.
- A failed hidden-fields request, or a response that is not a hidden-fields response, hides nothing (fail open). Instead of Umbraco's generic error notification for every document and block, the backoffice shows one warning headed "Property Visibility" until the page is reloaded.
- When an Umbraco version cannot remove tabs and groups, properties stay hidden and the backoffice shows one warning. The server logs a warning at startup on an Umbraco major other than 17.
- The package carries `LICENSE` and `THIRD-PARTY-NOTICES.md`, with the MIT notice of the Hey API client code in the backoffice bundle.

### Known limitations

Hiding changes what editors see, not what they may do: hidden values are still saved, readable and writable through the Management API, and returned by the Delivery API. Hidden mandatory properties still block publishing. Hidden fields can show for one round trip while the rules load. There are no rules per user group or per culture, and none for media or member types. The complete list is under [What it does not do](https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility#what-it-does-not-do) in the README.
