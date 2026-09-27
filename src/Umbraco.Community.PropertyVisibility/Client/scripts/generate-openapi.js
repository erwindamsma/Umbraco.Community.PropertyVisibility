import { createClient } from '@hey-api/openapi-ts';

// Regenerates src/api from the test site's Swagger document.
// Usage: node scripts/generate-openapi.js <swaggerUrlOrFile>
// Output location and plugins come from openapi-ts.config.ts (hey-api loads it from the working directory and
// merges the input given here over it), so the same settings apply to a plain `npx openapi-ts` run.

const input = process.argv[2];
if (!input) {
	console.error('ERROR: Missing URL (or file path) of the OpenAPI document.');
	console.error(
		'Example: node scripts/generate-openapi.js https://localhost:44300/umbraco/swagger/property-visibility/swagger.json',
	);
	process.exit(1);
}

// The test site runs on https with a self-signed development certificate.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

if (/^https?:\/\//i.test(input)) {
	console.log(
		'Ensure the test site is running (dotnet run --project src/Umbraco.Community.PropertyVisibility.TestSite --launch-profile https).',
	);
	console.log(`Fetching the OpenAPI document from ${input}`);

	let response;
	try {
		response = await fetch(input);
	} catch (error) {
		console.error(`ERROR: Could not reach the OpenAPI document: ${error instanceof Error ? error.message : error}`);
		console.error('The test site may not be running, or the URL in package.json (generate-client) is wrong.');
		process.exit(1);
	}

	if (!response.ok) {
		console.error(`ERROR: The OpenAPI document returned ${response.status} ${response.statusText}.`);
		console.error('The test site may not be running, or the URL in package.json (generate-client) is wrong.');
		process.exit(1);
	}

	console.log('OpenAPI document fetched.');
}

console.log('Generating the TypeScript client into src/api ...');
await createClient({ input });
console.log('Done. Review the diff in src/api and run npm run build.');
