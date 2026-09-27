# Security policy

## Supported versions

Security fixes go into the latest release of the current major version: 1.x, for Umbraco 17. Older 1.x releases, prereleases included, get no separate fixes; update to the latest 1.x.

## Reporting a vulnerability

Report vulnerabilities privately through a GitHub security advisory:
https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/security/advisories/new
(on the repository page: Security > Report a vulnerability).

Please do not open a public issue or pull request for a vulnerability, and do not include exploit details in one.

A useful report contains:

- the package version and the Umbraco version;
- what an attacker can do, and which access they need for it (none, a backoffice user without Content section access, an editor, ...);
- the steps to reproduce, and the configuration involved (replace real site names, keys and aliases, keep the structure).

The report stays private until a fix is released. Reports are handled on a best-effort basis by the maintainer, and reporters are credited in the advisory unless they prefer not to be.

## What counts as a vulnerability

Hiding a property, group or tab with this package is a backoffice user-interface feature, not authorization. By design:

- hidden values are loaded into the browser and saved with the document;
- hidden values are readable and writable through the Management API by every user who can edit the document;
- the package does not change the Delivery API or the website: they still return and render hidden values.

A report that a hidden value can be read or changed through these channels describes the intended behaviour, not a vulnerability. To restrict who may read or change a property, use Umbraco's user group permissions, including the Document Property Value permissions.

Also by design: the hidden-fields API (`/umbraco/property-visibility/v1/hiddenfields`) checks that the caller is an approved backoffice user with Content section access; it does not check the user's start nodes or document permissions. Any such user can send any document key and learn whether it exists or is in the recycle bin (the root resolution), the label of the site whose rules apply to it and why (key, name or default), and the documented warnings. Site labels must therefore not contain text that such users may not see. The health check and the log viewer, which show root node names and keys, the site labels and the path of the rules file, are available to every user with access to the Settings section, as all Umbraco health checks and logs are.

In scope, for example:

- the hidden-fields API answering a request without a valid backoffice login, from a backoffice user who is not approved, or from a user without Content section access;
- that API returning more than property and container keys, the matched site's label and match reason, the root resolution and the documented warnings; it must never return a root node's key or name;
- configuration values or content names that end up as unescaped markup in the health check or the backoffice;
- configuration or request input that takes the site down instead of failing open.

## Credentials in this repository

The only credentials in the repository are two documented dummies for the local SQLite test site, both in
`src/Umbraco.Community.PropertyVisibility.TestSite/appsettings.Development.json`: the unattended install login
(`admin@example.com` / `PropertyVisibilityDev1!`) and a fixed `Umbraco:CMS:Imaging:HMACSecretKey`. Neither protects
anything outside a developer's machine. They are listed as accepted exceptions in
[docs/testing.md](docs/testing.md#publication-checklist-accepted-exceptions).
