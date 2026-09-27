import {
	openDocument,
	recordHiddenFieldsRequests,
	renderedProperty,
	save,
	saveAndPublish,
	settle,
	startCreateUnder,
	waitForHiddenFields,
	waitForHiddenFieldsWithParent,
} from '../support/backoffice.js';
import { SAMPLE } from '../support/env.js';
import { expectCorporateLandingPage } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getDocument, getHiddenFields, resolveSample, valueOf } from '../support/management-api.js';

/** Every property of a Corporate landingPage that the package hides (spec 04 checks the full set against the API). */
const HIDDEN_ALIASES = [
	'bannerImage',
	'relatedLinks',
	'metaTitle',
	'metaDescription',
	'metaKeywords',
	'legacyRedirect',
	'enableExperimental',
];

test.describe('Hidden values survive saving and publishing', () => {
	test('existing Corporate landing: save -> publish -> save keeps every hidden value', async ({ page }) => {
		const keys = await resolveSample(page);
		const before = await getDocument(page, keys.corporateLanding);
		const hiddenBefore = Object.fromEntries(HIDDEN_ALIASES.map((alias) => [alias, valueOf(before, alias)]));
		// The seeded values the acceptance item names.
		expect(hiddenBefore.legacyRedirect).toBeTruthy();
		expect(hiddenBefore.metaKeywords).toBeTruthy();
		expect(hiddenBefore.enableExperimental).toBe(true);

		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);

		await save(page);
		await saveAndPublish(page);
		await settle(page);
		await save(page);

		// Read back what the server stored (independent of the package's hiding).
		const after = await getDocument(page, keys.corporateLanding);
		const hiddenAfter = Object.fromEntries(HIDDEN_ALIASES.map((alias) => [alias, valueOf(after, alias)]));
		expect(hiddenAfter).toEqual(hiddenBefore);

		// Reopen: still hidden.
		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);
	});

	test('new landingPage under Corporate site: hidden before saving, preset in a hidden group survives save -> publish -> save', async ({ page }) => {
		const keys = await resolveSample(page);

		// Tree create action on "Corporate site" -> "Landing page".
		const requests = recordHiddenFieldsRequests(page);
		const withParent = waitForHiddenFieldsWithParent(page, keys.corporateSite);
		await startCreateUnder(page, SAMPLE.corporateSite, SAMPLE.landingPageTypeName);

		const created = await withParent;
		expect(created.status()).toBe(200);
		const scaffoldKey = new URL(created.url()).searchParams.get('documentKey')!;

		// The very first request for the scaffold already carries the parent (not only a later re-anchor).
		const forScaffold = requests.filter((url) => url.searchParams.get('documentKey') === scaffoldKey);
		expect(forScaffold.length).toBeGreaterThan(0);
		expect(forScaffold[0].searchParams.get('parentKey'), 'parentKey of the first request for the new document').toBe(
			keys.corporateSite,
		);
		const createdBody = await created.json();
		expect(createdBody.rootResolution).toBe('Parent');
		expect(createdBody.matchedSite).toEqual(expect.objectContaining({ label: 'corporate', reason: 'Key' }));
		await settle(page);

		// Before the first save: hidden exactly like the existing Corporate landing page.
		await expectCorporateLandingPage(page);

		const name = `Acceptance landing ${Date.now()}`;
		await page.locator('uui-input[data-mark="input:entity-name"] input').fill(name);
		await renderedProperty(page, 'title').locator('input').first().fill('Created by the acceptance suite');

		// First save creates the document under the scaffold key; the workspace re-requests without parentKey.
		const afterSave = waitForHiddenFields(page, scaffoldKey);
		await save(page);
		await page.waitForURL(new RegExp(`/workspace/document/edit/${scaffoldKey}`));
		const saved = await afterSave;
		expect(new URL(saved.url()).searchParams.get('parentKey')).toBeNull();
		await settle(page);
		await expectCorporateLandingPage(page);

		await saveAndPublish(page);
		await settle(page);
		await save(page);

		const document = await getDocument(page, scaffoldKey);
		expect(document.variants[0]?.name).toBe(name);
		expect(valueOf(document, 'title')).toBe('Created by the acceptance suite');
		// The True/False (default on) preset lives in the hidden settingsTab/advanced group.
		expect(valueOf(document, 'enableExperimental')).toBe(true);

		const { body } = await getHiddenFields(page, { documentKey: scaffoldKey, contentTypeKey: keys.landingPageType });
		expect(body.rootResolution).toBe('Document');
		expect(body.matchedSite).toEqual(expect.objectContaining({ label: 'corporate', reason: 'Key' }));

		// Reopen: still hidden.
		await openDocument(page, scaffoldKey);
		await expectCorporateLandingPage(page);
	});
});
