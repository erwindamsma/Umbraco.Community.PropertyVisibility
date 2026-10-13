import { openApiDocumentPath } from '../support/env.js';
import { expect, test } from '../support/fixtures.js';
import { umbracoMajor } from '../support/management-api.js';

const HTTP_METHODS = ['get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace'];

test.describe('The package OpenAPI document', () => {
	test('lists exactly one operation: GET hiddenfields', async ({ page, request }) => {
		const response = await request.get(openApiDocumentPath(await umbracoMajor(page)));
		expect(response.status()).toBe(200);

		const document = (await response.json()) as { paths: Record<string, Record<string, unknown>> };
		const operations = Object.entries(document.paths).flatMap(([path, item]) =>
			Object.keys(item)
				.filter((key) => HTTP_METHODS.includes(key))
				.map((method) => `${method.toUpperCase()} ${path}`),
		);

		expect(operations).toHaveLength(1);
		expect(operations[0]).toMatch(/^GET \/umbraco\/property-visibility\/v1\/hiddenfields$/i);
	});
});
