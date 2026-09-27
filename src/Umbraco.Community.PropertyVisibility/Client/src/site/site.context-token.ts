import type { UmbPropertyVisibilitySiteContext } from './site.context.js';
import { SITE_CONTEXT_ALIAS } from '../constants.js';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';

/**
 * Provided on the document workspace by the package's document workspace context and consumed by the
 * block workspace context. The alias is unique to this package, so no core workspace context provider
 * intercepts the request and `passContextAliasMatches` is not needed across the block modal proxy.
 */
export const UMB_PROPERTY_VISIBILITY_SITE_CONTEXT = new UmbContextToken<UmbPropertyVisibilitySiteContext>(
	SITE_CONTEXT_ALIAS,
);
