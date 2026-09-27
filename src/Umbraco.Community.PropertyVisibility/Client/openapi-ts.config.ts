import { defineConfig, defaultPlugins } from '@hey-api/openapi-ts';

/**
 * Generates the typed API client in src/api from the package's own Swagger document.
 * Used by scripts/generate-openapi.js (npm run generate-client) and by `npx openapi-ts` directly.
 * The test site must be running on https://localhost:44300 (see src/Umbraco.Community.PropertyVisibility.TestSite).
 */
export default defineConfig({
	input: 'https://localhost:44300/umbraco/swagger/property-visibility/swagger.json',
	output: 'src/api',
	plugins: [
		...defaultPlugins,
		{
			name: '@hey-api/client-fetch',
			// No server URL in the generated client: the swagger document names the dev test site, which must never
			// ship. Requests are same-origin until the entry point's configureClient sets the backoffice base URL.
			baseUrl: false,
			runtimeConfigPath: '../hey-api',
		},
		{
			name: '@hey-api/sdk',
			asClass: true,
			classNameBuilder: '{{name}}Service',
		},
	],
});
