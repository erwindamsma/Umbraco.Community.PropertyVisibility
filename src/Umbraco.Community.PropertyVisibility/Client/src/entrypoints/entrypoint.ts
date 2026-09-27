import { client } from '../api/client.gen.js';
import { LOG_PREFIX } from '../constants.js';
import { describeError } from '../debug.js';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from '@umbraco-cms/backoffice/extension-api';

/**
 * Wires the generated API client into the backoffice auth context.
 * configureClient (Umbraco 17.3+) binds the backoffice base URL, credentials, the bearer callback with token refresh
 * and the default 401 / problem-details interceptors. Until then the client carries umbHttpClient's configuration
 * (see hey-api.ts): cookie credentials and a same-origin base URL, so a request fired before this resolves still
 * authenticates against the site that serves the backoffice.
 */
export const onInit: UmbEntryPointOnInit = async (host) => {
	// getContext rejects when no auth context answers in time (Umbraco 17.6.2) instead of resolving to undefined.
	let authContext: typeof UMB_AUTH_CONTEXT.TYPE | undefined;
	let lookupError: unknown;
	try {
		authContext = await host.getContext(UMB_AUTH_CONTEXT);
	} catch (error) {
		lookupError = error;
	}

	if (!authContext) {
		console.warn(
			`${LOG_PREFIX} UMB_AUTH_CONTEXT is not available; API requests keep the copied umbHttpClient configuration (same origin, cookie credentials).`,
			...(lookupError === undefined ? [] : [describeError(lookupError)]),
		);
		return;
	}

	authContext.configureClient(client);
};

export const onUnload: UmbEntryPointOnUnload = () => {
	// Nothing to release: the client configuration lives for the page lifetime, like umbHttpClient's own.
};
