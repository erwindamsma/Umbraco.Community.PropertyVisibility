/** The test site; override with PV_BASE_URL. */
export const BASE_URL = process.env.PV_BASE_URL ?? 'https://localhost:44300';

/**
 * The test site's unattended-install administrator (src/Umbraco.Community.PropertyVisibility.TestSite/appsettings.Development.json).
 * A documented dummy credential for a local SQLite database; override with PV_ADMIN_EMAIL / PV_ADMIN_PASSWORD.
 */
export const ADMIN_EMAIL = process.env.PV_ADMIN_EMAIL ?? 'admin@example.com';
export const ADMIN_PASSWORD = process.env.PV_ADMIN_PASSWORD ?? 'PropertyVisibilityDev1!';

/** The package's backoffice API. */
export const HIDDEN_FIELDS_PATH = '/umbraco/property-visibility/v1/hiddenfields';
export const SWAGGER_PATH = '/umbraco/swagger/property-visibility/swagger.json';

/** Aliases the package registers (Client/src/constants.ts, Client/public/umbraco-package.json). */
export const PACKAGE_ID = 'Umbraco.Community.PropertyVisibility';
export const BUNDLE_ALIAS = 'Umbraco.Community.PropertyVisibility.Bundle';
export const DOCUMENT_WORKSPACE_CONTEXT_ALIAS = 'Umbraco.Community.PropertyVisibility.WorkspaceContext.Document';
export const BLOCK_WORKSPACE_CONTEXT_ALIAS = 'Umbraco.Community.PropertyVisibility.WorkspaceContext.Block';
export const SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS = 'Umbraco.Community.PropertyVisibility.WorkspaceContext.SiteBoundary';
export const ENTRY_POINT_ALIAS = 'Umbraco.Community.PropertyVisibility.EntryPoint';

/** The window event the applier dispatches after every completed pass (Client/src/constants.ts APPLIED_EVENT_NAME). */
export const APPLIED_EVENT = 'umbraco-community-property-visibility:applied';

/**
 * Header the suite's own in-page API calls carry (support/management-api.ts), so the applied-event tracker can tell
 * them apart from the package's requests: a direct call to the hidden-fields API is never applied.
 */
export const DIRECT_CALL_HEADER = 'x-pv-acceptance-direct';

/** Names of the seeded content (uSync/v17 in the test site). */
export const SAMPLE = {
	corporateSite: 'Corporate site',
	campaignSite: 'Campaign site',
	corporateLanding: 'Corporate landing',
	campaignLanding: 'Campaign landing',
	corporateArticle: 'Corporate article',
	campaignArticle: 'Campaign article',
	landingPageTypeName: 'Landing page',
	/** The culture-variant document (document type variantPage) under Corporate site, published in en-US and da-DK. */
	corporateVariantPage: 'Corporate variant page',
	/** A media item (media type blockMedia) whose inline Block List holds a promoBanner; the Corporate landing mainBlocks promoBanner picks it as its image. */
	promoMedia: 'Promo media',
} as const;
