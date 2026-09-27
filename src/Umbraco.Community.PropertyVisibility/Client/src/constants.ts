/** Root alias of every extension, context and rule this package registers. */
export const PACKAGE_ALIAS = 'Umbraco.Community.PropertyVisibility';

/** Prefix of the property view guard rule uniques; the property type key is appended. */
export const RULE_PREFIX = `${PACKAGE_ALIAS}.Property.`;

/** Set localStorage[DEBUG_STORAGE_KEY] = '1' to log every hidden-fields response to the browser console. */
export const DEBUG_STORAGE_KEY = `${PACKAGE_ALIAS}.Debug`;

/** Prefix of every console message written by this package. */
export const LOG_PREFIX = '[PropertyVisibility]';

/**
 * Name of the window CustomEvent dispatched after every completed apply pass (see applier/applied-event.ts).
 * A public, stable integration point: the name and the detail shape only change in a major version.
 */
export const APPLIED_EVENT_NAME = 'umbraco-community-property-visibility:applied';

export const ENTRY_POINT_ALIAS = `${PACKAGE_ALIAS}.EntryPoint`;
export const DOCUMENT_WORKSPACE_CONTEXT_ALIAS = `${PACKAGE_ALIAS}.WorkspaceContext.Document`;
export const BLOCK_WORKSPACE_CONTEXT_ALIAS = `${PACKAGE_ALIAS}.WorkspaceContext.Block`;
export const SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS = `${PACKAGE_ALIAS}.WorkspaceContext.SiteBoundary`;
export const LOCALIZATION_EN_ALIAS = `${PACKAGE_ALIAS}.Localization.En`;
export const SITE_CONTEXT_ALIAS = `${PACKAGE_ALIAS}.SiteContext`;
