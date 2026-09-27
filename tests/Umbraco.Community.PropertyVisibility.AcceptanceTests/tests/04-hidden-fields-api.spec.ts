import { request as playwrightRequest } from '@playwright/test';
import { BASE_URL, HIDDEN_FIELDS_PATH, SAMPLE } from '../support/env.js';
import { expectedHidden, keyOfContainer, keysOfAliases, RULES } from '../support/expected.js';
import { expect, test } from '../support/fixtures.js';
import { getContentTypeStructure, getHiddenFields, resolveSample } from '../support/management-api.js';

test.describe('The hidden-fields API (GET /umbraco/property-visibility/v1/hiddenfields)', () => {
	test('returns 401 without a backoffice session', async () => {
		// A fresh request context: no cookies, no bearer.
		const anonymous = await playwrightRequest.newContext({ baseURL: BASE_URL, ignoreHTTPSErrors: true });
		try {
			const response = await anonymous.get(
				`${HIDDEN_FIELDS_PATH}?documentKey=${crypto.randomUUID()}&contentTypeKey=${crypto.randomUUID()}`,
			);
			expect(response.status()).toBe(401);
		} finally {
			await anonymous.dispose();
		}
	});

	test('returns the Corporate landing page keys to an authenticated backoffice page', async ({ page }) => {
		const keys = await resolveSample(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);

		const { status, body } = await getHiddenFields(page, {
			documentKey: keys.corporateLanding,
			contentTypeKey: keys.landingPageType,
		});
		expect(status).toBe(200);

		// The named keys from the acceptance item...
		const namedProperties = keysOfAliases(landingPage, RULES.corporateLandingPage.properties);
		const namedContainers = ['seoTab', 'seoTab/meta', 'settingsTab/advanced'].map((alias) => keyOfContainer(landingPage, alias));
		expect(body.propertyTypeKeys).toEqual(expect.arrayContaining(namedProperties));
		expect(body.containerKeys).toEqual(expect.arrayContaining(namedContainers));

		// ...and the full sets: the properties inside the hidden containers, plus emptied containers if any.
		const expected = expectedHidden(landingPage, RULES.corporateLandingPage);
		expect([...body.propertyTypeKeys].sort()).toEqual(expected.propertyKeys);
		expect([...body.containerKeys].sort()).toEqual(expected.containerKeys);
		expect(body.propertyTypeKeys).toEqual(
			expect.arrayContaining(keysOfAliases(landingPage, ['metaTitle', 'metaDescription', 'metaKeywords', 'legacyRedirect', 'enableExperimental'])),
		);

		expect(body.disabled).toBe(false);
		expect(body.rootResolution).toBe('Document');
		expect(body.matchedSite).toEqual(expect.objectContaining({ label: 'corporate', reason: 'Key' }));
		// By design: any Content section user can call the API for any document key, so the response never maps a
		// document to its site root, by key or by name.
		expect(body.matchedSite).not.toHaveProperty('rootNodeKey');
		expect(body.matchedSite).not.toHaveProperty('rootNodeName');
		expect(JSON.stringify(body)).not.toContain(keys.corporateSite);
		expect(JSON.stringify(body)).not.toContain(SAMPLE.corporateSite);
		expect(body.warnings).toEqual([]);
	});

	test('returns only metaKeywords for the Campaign landing page, matched by name', async ({ page }) => {
		const keys = await resolveSample(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);

		const { status, body } = await getHiddenFields(page, {
			documentKey: keys.campaignLanding,
			contentTypeKey: keys.landingPageType,
		});

		expect(status).toBe(200);
		expect(body.propertyTypeKeys).toEqual(keysOfAliases(landingPage, ['metaKeywords']));
		expect(body.containerKeys).toEqual([]);
		expect(body.matchedSite).toEqual(expect.objectContaining({ label: 'campaign', reason: 'Name' }));
		expect(body.matchedSite).not.toHaveProperty('rootNodeKey');
		expect(body.matchedSite).not.toHaveProperty('rootNodeName');
		expect(JSON.stringify(body)).not.toContain(keys.campaignSite);
		expect(JSON.stringify(body)).not.toContain(SAMPLE.campaignSite);
		expect(body.warnings).toEqual([]);
	});
});
