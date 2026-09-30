# Configuration

Property Visibility reads its rules from two places:

- the `PropertyVisibility` section of appsettings (any configuration source works: `appsettings.json`, environment variables, and so on);
- an optional rules file, `PropertyVisibility.config.json` next to `appsettings.json` by default (the `ConfigFile` option).

When the rules file exists and holds valid JSON, its rules replace the appsettings rules (see [Where the rules come from](#where-the-rules-come-from)). Both sources are reloaded without a restart.

## Single site

Top-level `ContentTypes` apply on every root node:

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

Save the file and reload the backoffice. The keys are content type aliases: document types, and element types used by blocks (see [Blocks](#blocks)). A key applies to documents and blocks of that type and of every type composed of it (see [Compositions](#compositions)).

## Multi-site

`Sites` adds rules per site. The label (`corporate`, `campaign`, ...) is free text used in diagnostics; it may not contain `:`.

```json
{
  "PropertyVisibility": {
    "Enabled": true,
    "HideEmptiedContainers": true,
    "ContentTypes": {
      "siteSettings": { "Containers": ["legacyTab"] }
    },
    "Sites": {
      "corporate": {
        "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b",
        "RootNodeName": "Corporate site",
        "ContentTypes": {
          "landingPage": { "Properties": ["bannerImage", "relatedLinks"], "Containers": ["seoTab", "settingsTab/advanced"] },
          "article": { "Containers": ["shareTab"] }
        }
      },
      "campaign": {
        "RootNodeName": "Campaign site",
        "ContentTypes": {
          "landingPage": { "Properties": ["metaKeywords"] }
        }
      },
      "everythingElse": {
        "IsDefault": true,
        "ContentTypes": {
          "landingPage": { "Properties": ["bannerImage"] }
        }
      }
    }
  }
}
```

The rules for a request are the top-level `ContentTypes` entries for the content type and for each of its compositions, united with the matched site's entries for the same types and those of the rule sets the site includes (see [Rule sets](#rule-sets)). Hiding is a union: a rule can only hide more, never show something another rule hides.

## Rule sets

Rules that several sites share can be written once, under `RuleSets`, and named in the `Include` of each site that uses them. The name (`simplePages`, ...) is free text; it may not contain `:`.

```json
{
  "PropertyVisibility": {
    "RuleSets": {
      "simplePages": {
        "ContentTypes": {
          "landingPage": { "Containers": ["seoTab", "settingsTab/advanced"] },
          "article": { "Containers": ["shareTab"] }
        }
      }
    },
    "Sites": {
      "corporate": {
        "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b",
        "Include": ["simplePages"],
        "ContentTypes": {
          "landingPage": { "Properties": ["bannerImage", "relatedLinks"] }
        }
      },
      "campaign": {
        "RootNodeName": "Campaign site",
        "Include": ["simplePages"]
      }
    }
  }
}
```

Both sites hide the Seo tab and the Advanced group of a landing page and the Share tab of an article; Corporate site also hides the landing page's banner image and related links.

- A site's rules are its own `ContentTypes` united with those of every rule set in its `Include`, and with the top-level `ContentTypes`. A rule set can only add to what a site hides.
- `Include` names are matched case-insensitively. A name that matches no rule set is a configuration error (`PV009`): nothing is hidden until it is fixed.
- A rule set cannot include another rule set.
- The health check checks each rule set's entries once, however many sites include it, and reports a rule set that no site includes (`PV106`).

## Roots that are not sites

An installation with several sites often has root nodes that are not sites, such as a settings, tags or shared content root. Once `Sites` is not empty, the health check reports every root that no site matches (`PV103`). For a root that is not a site, add a site with only its `RootNodeKey`:

```json
"settings": { "RootNodeKey": "d3f1b2a4-6c5e-4f7a-8b9c-0e1d2c3b4a59" }
```

Its documents keep getting only the top-level `ContentTypes`, and a root added later that no site matches, such as a new site without rules yet, is still reported.

A site marked `IsDefault` also ends `PV103`, but it takes every root that no other site matches, roots added later included, and applies its rules there. Use it for rules that should apply to all those roots, not to silence the warning.

## Compositions

A rule keyed by a composition applies wherever the composition is used: to the composition itself and to every document or element type composed of it, directly or through another composition. A parent document type counts as a composition, because Umbraco models a child type's parent that way.

- The rule resolves against the composition's own properties, tabs and groups, including what the composition gets from its own compositions. A container alias in it removes the composition's tab or group with that alias, not a tab or group with the same alias that the composing type adds itself or gets from another composition. The backoffice shows tabs with the same name as one tab, comparing names ignoring case, with runs of white space and the characters `_ . ! ~ * ( )` counted as `-` and apostrophes dropped (so "Legacy tab" and "legacy_tab" are one tab); when the composing type has a tab with the composition's tab name, that tab stays, with the composing type's own groups and properties in it.
- The rules keyed by the type itself and by all its compositions, top-level, for the matched site and in the rule sets it includes, are united. `HideEmptiedContainers` then looks at the type as the editor sees it, so a tab that a composition's rule empties disappears.
- An alias that does not exist on the composition is reported against the composition (`PV201`, `PV202`), in the API warnings and in the health check, even when the composing type has it. To hide something the composing type adds itself, key the rule by that type.
- The health check lists every rule keyed by a composition, with the types it reaches (`PV205`, informational).

In the samples on this page, `siteSettings` is a composition of the test site's `site` document type and holds a tab named "Legacy tab" (alias `legacyTab`) with its General group (`siteTitle`, `footerText`). The `site` type has a tab with the same name of its own, holding `siteNotes`, and the backoffice shows the two as one Legacy tab. The top-level rule

```json
"siteSettings": { "Containers": ["legacyTab"] }
```

therefore removes the composition's copy of the Legacy tab, its General group and both properties from every `site` document, on every site. The Legacy tab stays, with `siteNotes` in it. A `"site": { "Containers": ["legacyTab"] }` rule would remove both copies, and with them the whole tab.

## Options

| Option | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | bool | `true` | Kill switch. When `false` nothing is hidden and the API reports `disabled`. Appsettings only. |
| `ConfigFile` | string | `PropertyVisibility.config.json` | Rules file, relative to the content root (an absolute path also works). An empty value turns the file source off. A missing file, or one with no JSON value (empty or only comments), is not an error: the appsettings rules apply. The file is watched and reloaded without a restart. Appsettings only. |
| `HideEmptiedContainers` | bool | `true` | Also hide a group whose properties are all hidden, and a tab whose own properties and groups are all hidden. |
| `ContentTypes` | object | empty | Rules for every site, keyed by content type alias. A key also reaches the types composed of it ([Compositions](#compositions)). |
| `RuleSets` | object | empty | Rules that several sites share, keyed by a free name ([Rule sets](#rule-sets)). |
| `RuleSets:<name>:ContentTypes` | object | empty | Rules of this set, keyed by content type alias. A key also reaches the types composed of it. |
| `Sites` | object | empty | Rules per site, keyed by a free label. |
| `Sites:<label>:RootNodeKey` | GUID | none | Key of the site's root node. Survives renames; the recommended identity. |
| `Sites:<label>:RootNodeName` | string | none | Name of the site's root node (case-insensitive, trimmed). On a variant site: the default-culture name as of the last save. |
| `Sites:<label>:IsDefault` | bool | `false` | The site whose rules apply to every root no other site matched. At most one. |
| `Sites:<label>:Include` | string[] | empty | Names of the rule sets this site uses, matched case-insensitively. Their rules are united with the site's own. |
| `Sites:<label>:ContentTypes` | object | empty | Rules for this site, keyed by content type alias. A key also reaches the types composed of it. |
| `<content type>:Properties` | string[] | empty | Property aliases to hide (own or from a composition). |
| `<content type>:Containers` | string[] | empty | Tabs and groups to hide, see the alias grammar. |

Aliases (content types, properties, containers) are matched case-insensitively. Configuration keys bind case-insensitively too.

## Where the rules come from

| Situation | Rules in effect |
|---|---|
| No rules file (missing, empty, only comments, or `ConfigFile` is empty) | The appsettings `ContentTypes`, `RuleSets` and `Sites`. |
| The rules file exists and is valid | The file's `ContentTypes`, `RuleSets` and `Sites` replace the appsettings ones wholesale (replace, not merge). `HideEmptiedContainers` from the file overrides appsettings only when the file sets it. |
| The rules file exists but is invalid, too large or locked (`PV001`, `PV002`) | The last valid version of the same file: the last version that parsed and passed validation. If there is none, the file contributes no rules and the appsettings rules stay replaced, so nothing is hidden by rules until the file is fixed. |

`Enabled` and `ConfigFile` can only be set in appsettings. When appsettings defines `ContentTypes`, `RuleSets` or `Sites` while the rules file exists, the file wins and `PV301` is logged as a warning, once per file content.

## The rules file

The rules file holds the same shape and names as the appsettings section, without the `PropertyVisibility` wrapper:

```json
{
  "$schema": "./PropertyVisibility.config-schema.json",
  "HideEmptiedContainers": true,
  "ContentTypes": {
    "siteSettings": { "Containers": ["legacyTab"] }
  },
  "Sites": {
    "corporate": {
      "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b",
      "ContentTypes": {
        "landingPage": { "Containers": ["seoTab"] }
      }
    }
  }
}
```

- Allowed keys: `$schema` (optional, ignored when loading), `HideEmptiedContainers`, `ContentTypes`, `RuleSets` and `Sites`. `Enabled` and `ConfigFile` are rejected (`PV002`), and so is a `PropertyVisibility` wrapper around the rules.
- Keys are case-insensitive, so camelCase works. Comments, trailing commas and a UTF-8 byte order mark are accepted. A key given twice, even when the two differ only in case, is `PV001`.
- Save the file as UTF-8. A file with a UTF-16 or UTF-32 byte order mark (what Windows PowerShell 5.1 `>` and `Out-File` write) is transcoded and loads too. The file may be at most 1 MiB; a larger one is `PV001` and is not read. A file whose size the file system does not report, such as a device or `/proc` file, is read only up to that limit.
- An invalid file is reported with enough detail to fix it: `PV001` for invalid JSON (with line and position), a file that is not valid UTF-8, for example one saved as Windows-1252 (with the line and position of the first invalid byte), a value of the wrong type (with its JSON path), a duplicate key, a file over 1 MiB or a file that cannot be read; `PV002` for an unknown key, with its JSON path and a "Did you mean" suggestion. Every unknown key in the file is listed, not only the first. One error is logged per distinct broken content, and the last valid version of the file stays in effect.
- A file that is still locked by the program that wrote it is read again automatically after 1, 2 and 5 seconds, then every 30 seconds until it can be read; releasing a lock raises no file change event, so without the retry the edit would wait for the next change. A file that cannot be read for a reason that time does not fix (access denied, a path that is too long) is not retried: it is read again on the next change of the file or of appsettings, or after a restart.
- Rules that parse but break validation (`PV003` to `PV007`, `PV009`, `PV010`) behave like invalid appsettings: `PV008`, nothing is hidden (fail open) until they are fixed. Such a version never becomes the "last valid version": after a later syntax error the version before it applies again.

The test site ships the sample above as `src/Umbraco.Community.PropertyVisibility.TestSite/PropertyVisibility.config.sample.json`, so appsettings stays its active source. Copy it to `PropertyVisibility.config.json` to switch the test site to the file.

## Reloading without a restart

- Rules file: edits are picked up 250 ms after the last write (debounced), as are creating and deleting the file. Changing `ConfigFile` in appsettings moves the watch to the new file. A file outside the content root (an absolute path or one with `..`, for example a mounted volume) is watched through its own folder, which must exist when the watch starts.
- When the watch cannot start or stops (the folder does not exist or cannot be read, or the system's file watcher limit is reached, as with an exhausted inotify instance limit on Linux), the site keeps working and the file is still read on every rebuild, but edits take effect only after a restart or an appsettings change. The server log gets one warning and the health check shows `PV304`; the next appsettings reload tries to watch the file again.
- Appsettings: the host reloads `appsettings.json` on change (`reloadOnChange`), and the new options apply to the next request.
- The rules file is watched with the .NET file watcher. On Docker bind mounts, volumes and network shares, file change events may not arrive; set the environment variable `DOTNET_USE_POLLING_FILE_WATCHER=1` (or `true`) so edits are picked up (polling picks an edit up within about 4 to 8 seconds).
- A rules file that is a symbolic link (a Kubernetes ConfigMap or Secret, or a link into a shared release folder) is not reloaded by the default watcher, and no `PV304` is raised: point `ConfigFile` at the real file, or set `DOTNET_USE_POLLING_FILE_WATCHER=1`.
- The content root's file watcher ignores files whose name starts with a dot and files with the Hidden or System attribute, so such a rules file gets a watcher of its own on its folder. The attribute is checked when the watch starts: when a file in the content root only gets the Hidden or System attribute afterwards, its edits are not picked up until the next appsettings change or restart, and no `PV304` is raised.
- The watch covers the rules file's folder and its subfolders. With the default `ConfigFile` that is the content root, also when no rules file exists, and `ConfigFile: "../rules.json"` watches the whole parent folder. Keep a rules file outside the content root in a small folder of its own, and set `ConfigFile` to `""` when you do not use a rules file.
- The backoffice asks for the hidden fields each time a document opens, so reopen the document (or reload the backoffice) to see a change. Blocks reuse the responses of their document, so reopening only a block's modal is not enough.

## Container alias grammar

| Alias | Hides |
|---|---|
| `seoTab` | the tab, every group under it and all their properties |
| `settingsTab/advanced` | one group inside a tab |
| `notes` | a root-level group (a group that is not inside a tab) |

A group inside a tab is only addressed by its full `tab/group` path; its local alias alone does not match. The tab alias ends at the first `/`, and the group alias after it may contain `/` itself: Umbraco builds a group's alias from its tab's alias and its own name, so the group "Image/Video" in the tab "Media" is `media/image/Video`. An alias with nothing before or after its first `/` is a configuration error (`PV006`).

## Blocks

Rules keyed by an element type alias apply inside blocks, exactly like rules for a document type: under the top-level `ContentTypes` for every site, under a site's `ContentTypes`, or in a rule set. Properties, tabs and groups are hidden the same way as on documents, including `HideEmptiedContainers`.

```json
"corporate": {
  "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b",
  "ContentTypes": {
    "promoBanner": { "Properties": ["overlayColour"] },
    "promoBannerSettings": { "Properties": ["anchorId"] }
  }
}
```

- The site is the one of the document that holds the block. A block in a document that has not been saved yet uses the site of the parent the document is created under, and its rules are requested again after the first save.
- A block has two halves, and each follows the rules of its own element type: the Content view uses the block's content element type (`promoBanner` above), the Settings view its settings element type (`promoBannerSettings`). A block type without a settings element type has no Settings half and requests nothing for it.
- Each element type is requested once per document: the Content and Settings halves, every inline block and every nested block of the same type share the response, and closing and reopening a block's modal applies the rules again without a new request. A change to the rules therefore reaches blocks when the document is opened again, as for the document itself.

Where it works (Umbraco 17.6.2 and 17.7.0):

| Block editor | Editing | Covered by |
|---|---|---|
| Block List | in a modal, and inline (`useInlineEditingAsDefault`) | Acceptance specs `13`, `14`, `16`, `19` |
| Block Grid | in a modal | Acceptance specs `15`, `16` |
| Blocks inside a block's property (a Block List in a block, at any depth) | in a modal (one modal per level) | Acceptance spec `17` (two levels) |
| Blocks in a document that is not saved yet | in a modal, and inline | Acceptance spec `18` |
| Blocks with a hidden mandatory property that fails validation | Save and publish of the document | Acceptance spec `20` |
| Blocks in a media item (nothing hidden, also when it is opened from a document) | inline, in the media workspace | Acceptance spec `21` |
| Blocks in a culture-variant document, in split view | in a modal, opened from either culture | Acceptance spec `23` |
| Single Block | in a modal, and inline | Manual run 2026-09-27 ([testing.md](testing.md#manual-acceptance-runs)) |
| Blocks in the rich text editor (Tiptap) | in a modal | Manual run 2026-09-27 ([testing.md](testing.md#manual-acceptance-runs)) |
| Blocks pasted from the clipboard into another site's document | in a modal | Manual run 2026-09-27 ([testing.md](testing.md#manual-acceptance-runs)) |

What it does not do:

- Blocks edited outside a document (in media, members or document blueprints) get nothing hidden: there is no document to resolve a site from, and nothing is requested. That also holds when the media item or member is opened in a modal from inside a document (for example with "Open in Media Library" from a media picker): the package stops the block at the media, member or blueprint workspace (`Umbraco.Community.PropertyVisibility.WorkspaceContext.SiteBoundary`), so the block never takes the rules of the document it was opened from. Spec `21` covers a media item; members and document blueprints use the same boundary and are manual cases in [testing.md](testing.md#manual-acceptance-runs).
- Block Grid inline editing (a block type with `inlineEditing` on): the property the grid renders inline, the element type's first property, is always shown, because Umbraco renders it without the property view guard. The block's modal still hides it, with everything else configured (checked on 17.6.2 and 17.7.0, see [testing.md](testing.md#manual-acceptance-runs)).
- A block label whose template refers to a hidden property still shows its value. Hiding is a backoffice feature only: hidden values stay in the block data and are saved with the document.

The [applied event](#integration-the-applied-event) reports each block half with `target` `block-content` or `block-settings`, the element type as `contentTypeKey` and the key of the document that holds the block as `documentKey`.

## Site matching

For each request the package resolves the document's root node (for a new document: its parent's root) and picks the site:

1. the site whose `RootNodeKey` is the root's key;
2. otherwise the site whose `RootNodeName` equals the root's name;
3. otherwise the `IsDefault` site;
4. otherwise no site: only the top-level `ContentTypes` apply.

A site with both a key and a name is matched by key only, as long as the key is a root node; when the root was renamed since, the response carries a `PV105` warning. Its name is then used for that check alone: if the stale name has become another root's name, that other root does not match the site (it falls through to the default site). When the key is not a root node (`PV101`), the name still matches. Documents in the recycle bin match no site, not even the default one: they get the top-level rules only.

## Invalid configuration

A configuration that cannot be bound (a value of the wrong type such as `"IsDefault": "yes"` or a `RootNodeKey` that is not a GUID, or an unknown key such as `RootNodeNme` in the appsettings section) or that fails validation never takes the site down: nothing is hidden, the API returns a `PV008` warning, the health check shows the error, and the server log gets one error per distinct problem with the exact key and value.

## JSON schemas and IntelliSense

The package ships two JSON schemas, generated from the options model:

- `appsettings-schema.Umbraco.Community.PropertyVisibility.json`: root `{ "PropertyVisibility": { ... } }`, merged into the site's `appsettings-schema.json`.
- `PropertyVisibility.config-schema.json`: the rules file (optional `$schema` string, `HideEmptiedContainers`, `ContentTypes`, `RuleSets`, `Sites`).

On the first `dotnet build` after `dotnet add package`, Umbraco's build targets copy both into the site project and add the appsettings schema to the site's `appsettings-schema.json`. Editors that follow the Umbraco template's `"$schema": "appsettings-schema.json"` line (Visual Studio, VS Code, Rider) then complete and check the `PropertyVisibility` section: the options, `RuleSets`, `Sites` and their fields, `ContentTypes`, `Properties` and `Containers`, with descriptions and defaults. For the rules file, start it with `"$schema": "./PropertyVisibility.config-schema.json"`; that schema is copied next to it, into the content root. To edit a rules file outside a site project, point `$schema` at the copy for your version on GitHub: `https://raw.githubusercontent.com/erwindamsma/Umbraco.Community.PropertyVisibility/v<version>/src/Umbraco.Community.PropertyVisibility/PropertyVisibility.config-schema.json`, with the tag of the version you installed (for example `v1.0.0-rc.1`).

- Both schemas are draft-04, like Umbraco's own. Option objects have `additionalProperties: false`, so an editor underlines a misspelled key. Dictionaries (`ContentTypes`, `RuleSets`, `Sites`) accept any key, so a rule set name in `Include` is only checked when the rules load (`PV009`).
- Property names are PascalCase. Loading is case-insensitive, so a camelCase key still works at runtime, but the editor marks it as not allowed.
- Defaults shown: `Enabled` true, `ConfigFile` `"PropertyVisibility.config.json"`, `HideEmptiedContainers` true, `IsDefault` false. In the rules file `HideEmptiedContainers` has no default: when unset, appsettings applies.
- The rules file schema rejects `Enabled` and `ConfigFile`, matching `PV002` at load time.
- The copied schema files are build output. The Umbraco template's `.gitignore` already ignores `appsettings-schema*.json`; add `PropertyVisibility.config-schema.json` to it.

## Health check

Settings > Health Check > Configuration > "Property Visibility configuration" checks the effective rules against this site.

- The first line is a summary: Success when there is no error and no warning, otherwise Info with the counts. It shows the rules source (the rules file with its full path, appsettings, or none), when the rules were last loaded successfully (UTC), the rules hash of that load (compare it between servers), the number of sites and root nodes, and the Umbraco version against the tested versions. A load whose rules fail validation is not a successful load: while the configuration is invalid, the time and hash are those of the last load that passed, labelled "Last successful load".
- After the summary comes one line per issue, errors first, each with its code, its configuration path and, where possible, a "Did you mean" suggestion.
- The health check is available to every backoffice user with access to the Settings section (Umbraco's rule for all health checks), not only to administrators. Unlike the hidden-fields API, it shows those users root node names and keys (in `PV101`, `PV102`, `PV103` and `PV105`; `PV101` and `PV102` list every root), the site labels and the full server path of the rules file. Give the Settings section only to users who may see them.
- Top-level `ContentTypes` entries and each rule set's entries are checked once, each site's entries once per site. An entry without properties and containers gets an informational `PV206` line and no other check, unless its content type does not exist (`PV104`). Property and container aliases are checked against the entry's own content type including its compositions, with the same case-insensitive matching the backoffice uses. For an entry keyed by a composition that is also the structure it resolves against in the types composed of it; the entry gets an informational `PV205` line listing those types.

## Logging

Configuration issues are logged once per configuration change, never per request: one run when Umbraco has started, one on every change of appsettings or the rules file, and one whenever a root node is created, renamed, moved, trashed or deleted (by design on every server of a load-balanced setup; load balancing is [not tested](compatibility.md#not-tested)). The runs happen in the background, about a second after the change; changes that arrive close together share one run. Their message format is `PropertyVisibility {IssueCode} at {ConfigPath}: {IssueMessage}`. Errors are logged at Error, warnings at Warning and informational issues at Information. A run whose rules did not change logs only issues that are new, such as a site that stopped matching after a root was renamed.

- `PV001`, `PV002` and `PV301` are logged by the rules file loader when it loads, once per distinct file content. `PV304` is logged by the file watcher when the watch fails.
- A configuration that cannot be read (`PV008`) is logged at startup, and by the hidden-fields service on the next request after a change.
- The loader, the watcher and the service start their lines with the code instead (`PV301: both appsettings ...`); a rules file that cannot be loaded gets one line with every code it has (`PV001, PV002: the PropertyVisibility rules file ... cannot be loaded; ...`). To find every line, search the log for the issue code or for `PropertyVisibility`.
- The hidden-fields service logs its site matching diagnostics (`PV103` per root, `PV105` per site) only at Debug; the warnings come from the configuration runs above.
- An unexpected failure of a hidden-fields request is logged at Error, with its stack trace, the first time it occurs; repeats of the same failure are logged at Debug until the configuration changes.

When you share log lines, for example in a bug report, paste the rendered messages, not the raw JSON entries. Replace root node names and keys (`PV101` and `PV102` list every root), server paths (`PV001` and `PV304` carry the full path of the rules file) and site labels you do not want to share, and leave out `MachineName`, `ProcessName`, `ProcessId` and `ApplicationId`, which every log entry carries.

## Issue codes

Codes never change meaning once released. The health check links every issue to this table.

| Code | Severity | Meaning | Typical fix | Reported by |
|---|---|---|---|---|
| `PV001` | Error | The rules file cannot be read or parsed: invalid JSON (with line and position), content that is not UTF-8 (with the line and position of the first invalid byte), a value of the wrong type (with its JSON path), a key given twice, a file over 1 MiB, or a file that cannot be opened (a locked file is read again automatically). The last valid version stays in effect. | Fix the JSON at the reported position or path; check that `ConfigFile` points at the rules file. | Log (when it loads), health check |
| `PV002` | Error | Unknown key in the rules file, with its JSON path and a "Did you mean" suggestion. The last valid version stays in effect. | Rename the key to the suggested one, or remove it. `Enabled` and `ConfigFile` belong in appsettings. | Log (when it loads), health check |
| `PV003` | Error | A site has none of `RootNodeKey`, `RootNodeName` or `IsDefault`. | Add `RootNodeKey` (recommended), `RootNodeName` or `IsDefault: true`. | Health check (with `PV008`), log |
| `PV004` | Error | Two sites share a `RootNodeKey` or a `RootNodeName`. | Give each root exactly one site, and merge the rules. | Health check (with `PV008`), log |
| `PV005` | Error | More than one site is marked `IsDefault`. | Keep `IsDefault` on one site. | Health check (with `PV008`), log |
| `PV006` | Error | A container alias is not `tab`, `tab/group` or `group`: it is empty, or empty before or after its first `/`. | Use a tab alias before the first `/` and a group alias after it, neither empty. | Health check (with `PV008`), log |
| `PV007` | Error | A site label contains `:`. | Rename the label. | Health check (with `PV008`), log |
| `PV008` | Error | The configuration cannot be bound (an unknown appsettings key, or a value of the wrong type; the binder's message is included) or fails validation (`PV003` to `PV007`, `PV009` and `PV010` listed separately). Nothing is hidden. | Fix the named key or value. | Health check, log (at startup, and by the service on the next request), API warning |
| `PV009` | Error | A site's `Include` names a rule set that does not exist under `RuleSets` (compared case-insensitively), with a "Did you mean" suggestion. | Correct the name, or add the rule set. | Health check (with `PV008`), log |
| `PV010` | Error | A rule set name contains `:`. | Rename the rule set and the names that include it. | Health check (with `PV008`), log |
| `PV101` | Warning | `RootNodeKey` is not a root node: the document is below a root, in the recycle bin, or does not exist. The issue lists every root with its name and key, and says whether the site still matches by name (or that another site holds that root by key) or as the default. | Use the root's key (suggested). | Health check, log |
| `PV102` | Warning | `RootNodeName` matches no root node (compared case-insensitively and trimmed), or only a root that another site holds by `RootNodeKey`, so the site never applies to it. When no root matches, the issue lists every root with its name and key. | Use the suggested root name, switch to `RootNodeKey`, or remove a site left behind after moving its rules to a site with a key. | Health check, log |
| `PV103` | Warning | A root node matches no site and no site is the default, so only the top-level rules apply to it. Only checked when `Sites` is not empty. | Add a site with the root's `RootNodeKey`; for a root that is not a site, give that site no rules ([Roots that are not sites](#roots-that-are-not-sites)). An `IsDefault` site ends the warning too, but takes every root no other site matches, roots added later included. | Health check, log, API warning (without key or name) |
| `PV104` | Warning | A configured content type alias is neither a document type nor an element type. | Use the suggested alias, or remove the entry. | Health check, log |
| `PV105` | Warning | A site matched by `RootNodeKey`, but its `RootNodeName` differs from the root's current name. When that name is now another root's name, the issue says so: the other root does not match this site. | Update `RootNodeName` to the suggested current name, or remove it. | Health check, log, API warning (without key or name) |
| `PV106` | Warning | A rule set is not included by any site, so its rules never apply. | Add it to the `Include` of the sites that should use it, or remove it. | Health check, log |
| `PV201` | Warning | A property alias does not exist on the content type (own properties and compositions checked). | Use the suggested alias, or remove it. | Health check, log, API warning |
| `PV202` | Warning | A container alias does not exist on the content type. The health check lists the available containers in `tab` and `tab/group` form, compositions included. | Use one of the listed aliases. | Health check, log, API warning |
| `PV203` | Warning | A hidden property (hidden by alias or by its container) is mandatory, so editors cannot fill it in and publishing still requires a value. Umbraco saves the document and reports "Document could not be published"; no tab or group is marked invalid, because the property is hidden (in a block too). | Make the property optional, or stop hiding it. | Health check, log |
| `PV204` | (API only) | The content type key of a hidden-fields request does not exist. | None; it is a request-time condition. | API warning |
| `PV205` | Info | A content type entry is keyed by a composition (or a parent document type), so its rules also apply to every type composed of it, directly or transitively; the issue names those types (the first ten, and how many more). There the rules hide only what the composition contributes ([Compositions](#compositions)). | None; check that the listed types are the ones the rule should reach. To hide something on one of them only, key the rule by that type. | Health check, log (Information) |
| `PV206` | Info | A content type entry lists no properties and no containers (or is `null`), so it hides nothing. An entry whose content type does not exist is `PV104` instead; an empty entry gets no other check (no `PV205`). | Add the aliases to hide, or remove the entry. | Health check, log (Information) |
| `PV301` | Warning | Appsettings defines `ContentTypes`, `RuleSets` or `Sites` while the rules file is in use; the file wins. | Keep the rules in one source. | Log (when it loads), health check |
| `PV302` | Info | The package is disabled (`Enabled: false`). The rules are still checked. | Set `Enabled` to `true`. | Health check, log (Information), API warning |
| `PV303` | Info for an untested 17.x, Warning for another major | The running Umbraco version is not in the tested list. | Check [compatibility.md](compatibility.md); report problems. | Health check, log |
| `PV304` | Warning | The rules file cannot be watched: its folder does not exist or cannot be read, or the system's file watcher limit is reached. The file is still read on every rebuild, but edits take effect after a restart or an appsettings change; the next appsettings reload tries to watch it again. | Create the folder, raise the watcher limit, or set `DOTNET_USE_POLLING_FILE_WATCHER=1`. | Log (when the watch fails), health check |

## The hidden-fields API

The backoffice calls `GET /umbraco/property-visibility/v1/hiddenfields?documentKey=&contentTypeKey=&parentKey=` (an approved backoffice user with Content section access; `parentKey` only for a document that is not saved yet). The endpoint does not check the user's start nodes or document permissions. Every well-formed request gets `200`, with empty lists when something fails; a key that is not a GUID gets `400`. The response:

| Field | Meaning |
|---|---|
| `propertyTypeKeys` | Keys of the property types to hide, including every property of a hidden container. |
| `containerKeys` | Keys of the tabs and groups to remove. |
| `disabled` | `true` when `Enabled` is `false`; both lists are then empty. |
| `rootResolution` | How the root node was resolved: `Document`, `Parent`, `None` or `RecycleBin`. |
| `matchedSite` | `{ "label", "reason" }` of the site whose rules applied (`reason`: `Key`, `Name` or `Default`), or `null` when only the top-level rules applied. |
| `warnings` | Issue-coded diagnostics for this request (`PV008`, `PV103`, `PV105`, `PV201`, `PV202`, `PV204`, `PV302`). A `PV201` or `PV202` from a rule keyed by one of the type's compositions names that composition. |

The backoffice shows a document's or block's fields until the response arrives, so hidden fields can render for that one round trip. When the request fails (a server error, a lost connection, or a `200` whose body is not a hidden-fields response, such as a proxy's HTML page), nothing is hidden for that document or block (fail open). Umbraco's generic error notification is turned off for this request; instead, the first failure after the backoffice is loaded shows one warning notification headed "Property Visibility" and writes one console warning with the error's name, message and HTTP status. A cancelled request (its workspace closed) and a `401` (Umbraco shows its own login) are not reported. Later failures are not reported again until the page is reloaded (with the [debug flag](#debugging-in-the-browser) they are logged), and every document or block that opens asks again.

Any approved backoffice user with Content section access can call the API for any document key, also for a document outside their start nodes, and learn whether the key exists or is in the recycle bin (`rootResolution`), which site's rules apply to it (`matchedSite` label and reason) and the warnings. Site labels are therefore visible to every Content section user: do not put text in them that those users may not see. The response never contains the key or name of a root node, in `matchedSite` or in a warning. Users with access to the Settings section find those in the health check, and the server log has them too (see [Health check](#health-check)).

## Debugging in the browser

Set `localStorage['Umbraco.Community.PropertyVisibility.Debug'] = '1'` in the backoffice (browser dev tools, Console). The flag is read each time something is logged, so it applies to the next response; reload to see the current document again. Remove the key or set it to anything else to turn logging off. With the flag on, the package writes `console.debug` lines prefixed with `[PropertyVisibility]`; show the Verbose level in the console to see them.

- One line per hidden-fields response. Its text ends with a summary, so a copied line, a screenshot or a captured log keeps it when the object after it shows as `Object`: `hidden fields for content type <key>: site 'corporate' (Key), 3 properties, 2 containers, no warnings`. Instead of the site it says `no site (root <resolution>)` when no site matched and `disabled` when the package is disabled; warnings are listed by code (`warnings PV201, PV202`). The object holds the document key, the content type key, how the parent was sent (`not sent (existing document)`, `sent (new document)`, `created at the content root`; the parent key itself is never logged), `disabled`, the root resolution, the matched site's label and match reason, the warnings and the number of property and container keys.
- A failed request logs its error name, message and HTTP status. Nothing is hidden in that case (fail open). Without the flag, only the first failure after the backoffice is loaded is written to the console, as a warning.
- One line per completed apply pass, with the counts in its text (`applied (document): 3 properties, 1 container`; `block-content` or `block-settings` for a block) and the event detail described below.
- A line when a response is dropped because the editor moved to another document first (for a block: to another document, another parent anchor or another element type).

The log never adds the key or name of the document's site root: the API does not return them, and the client logs only an explicit summary. When the open document is itself a root node, its own key is logged as the document key.

## Integration: the applied event

After every completed apply pass the backoffice dispatches a `CustomEvent` named `umbraco-community-property-visibility:applied` on `window`. A pass is complete when the property guard rules are in place and every tab and group removal for that pass has finished, including a replayed pass. The event fires for every response that is applied, also when there is nothing to hide: a disabled response (`Enabled: false`), no rules for the content type, a failed request, or a structure that failed to load. It does not fire for a response that is dropped because the editor moved on to another document (or the workspace was reset) before it arrived, nor for a request or structure load that never settles; when you wait for the event, use a timeout. It fires regardless of the debug flag.

It is a public, stable integration point: the name and the existing detail fields only change in a major version; new fields may be added in a minor version.

```js
window.addEventListener('umbraco-community-property-visibility:applied', (event) => {
  const { documentKey, contentTypeKey, propertyCount, containerCount, target } = event.detail;
});
```

| Field | Meaning |
|---|---|
| `documentKey` | The document the workspace edits. For a not-yet-saved document it is the scaffold key. A block reports the document it belongs to. |
| `contentTypeKey` | The document type, or for a block half the element type, whose rules were applied. |
| `propertyCount` | Number of property types hidden by view guard rules after this pass. |
| `containerCount` | Number of the response's tab and group keys that are no longer in the workspace structure after this pass. Normally all of them; fewer when hiding tabs and groups is unavailable in the running Umbraco version. |
| `target` | `document`, or `block-content` / `block-settings` for the Content and Settings halves of a block. |

The event can fire more than once for one document: every later structure emission (for example after a reload) re-runs the pass and reports again. Wait for the first event after your action, and filter on `documentKey` and `contentTypeKey`. The detail object is frozen.
