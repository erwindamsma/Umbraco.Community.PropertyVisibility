import type { CreateClientConfig } from './api/client.gen';
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';

/**
 * Initial configuration of the generated API client (called by client.gen.ts through runtimeConfigPath).
 *
 * Copies what the backoffice HTTP client sets for authentication and errors: `credentials: 'include'`, the
 * `[redacted]` bearer placeholder that makes the server read the HttpOnly token cookie, and `throwOnError`, so a request
 * fired before the entry point has run still authenticates. The generated client carries no server URL
 * (openapi-ts.config.ts sets `baseUrl: false`), and the core client has no module-level base URL either, so the
 * fallback is same-origin: requests go to the site that serves the backoffice. The entry point then calls
 * UmbAuthContext.configureClient, which sets the configured backoffice base URL and adds token refresh and the default
 * 401 / problem-details interceptors.
 *
 * The fields are picked one by one instead of spreading the whole configuration, so the serializers, headers and parse
 * mode stay those of this client's own generated runtime: Umbraco generates its client with the `@hey-api/openapi-ts`
 * version of each release (17.6.2 with an older one than this client), whose option types can differ from this client's.
 */
export const createClientConfig: CreateClientConfig = (config) => {
	const { auth, baseUrl, credentials, throwOnError } = umbHttpClient.getConfig();
	return {
		...config,
		auth,
		credentials,
		throwOnError,
		baseUrl: baseUrl ?? '',
	};
};
