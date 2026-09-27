import {
	settle,
	startCreateUnder,
	waitForHiddenFieldsWithParent,
	workspaceAction,
} from '../support/backoffice.js';
import {
	addBlockFromCatalogue,
	addInlineBlockFromCatalogue,
	blockEntry,
	expandInlineBlock,
	openBlockModal,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import { expectCorporateLandingPage } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getDocument, resolveSample, setPropertyMandatory } from '../support/management-api.js';

/**
 * A property the package hides together with its tab or group keeps pointing at the removed container (the applier
 * removes containers with `preventRemovingProperties`). When such a property fails validation, Umbraco 17.6.2's
 * validation-to-hints manager of the document workspace and of every block element manager cannot find the container
 * and throws inside an unhandled promise. The package marks exactly that rejection as handled
 * (Client/src/applier/removed-container-rejection.ts): Umbraco still blocks the publish with its own notification, and
 * the console stays clean.
 *
 * For the duration of the test two hidden properties are made mandatory through the Management API (a hidden mandatory
 * property is a configuration mistake the health check reports as PV203, so the sample does not ship one):
 * - landingPage.metaTitle, in the seoTab/meta group, whose seoTab tab is removed on Corporate site;
 * - promoBannerSettings.anchorId, hidden on Corporate site, whose Settings group HideEmptiedContainers removes.
 * Both are set back to their seeded value (not mandatory) in `finally`.
 *
 * Umbraco asks the server to validate before it saves; the server answers the invalid content with 400, and the browser
 * logs that response as "Failed to load resource". The test declares exactly that request (fixture
 * `expectedRequestFailures`) and checks it happened: it is the validation the test provokes, not an error.
 */
const VALIDATE_URL = /\/umbraco\/management\/api\/v1(\.1)?\/document(\/[0-9a-f-]{36})?\/validate$/;

test.describe('A hidden mandatory property that fails validation (document and blocks)', () => {
	test('a new landingPage under Corporate site with empty hidden mandatory properties: Save and publish is blocked with Umbraco\'s notification and no console error', async ({ page, consoleErrors, expectedRequestFailures }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		expectedRequestFailures.push({
			url: VALIDATE_URL,
			status: 400,
			reason: 'the server validation of a document with empty mandatory properties, provoked on purpose',
		});
		const rejectedValidations: string[] = [];
		page.on('response', (response) => {
			if (response.status() === 400 && VALIDATE_URL.test(new URL(response.url()).pathname)) rejectedValidations.push(response.url());
		});

		try {
			await setPropertyMandatory(page, keys.landingPageType, 'metaTitle', true);
			await setPropertyMandatory(page, types.promoBannerSettings.key, 'anchorId', true);

			const created = waitForHiddenFieldsWithParent(page, keys.corporateSite);
			await startCreateUnder(page, SAMPLE.corporateSite, SAMPLE.landingPageTypeName);
			const scaffoldKey = new URL((await created).url()).searchParams.get('documentKey')!;
			await settle(page);
			// The seoTab tab (with metaTitle's Meta group) is gone.
			await expectCorporateLandingPage(page);

			const name = page.locator('uui-input[data-mark="input:entity-name"] input');
			const documentName = `Acceptance validation ${Date.now()}`;
			await name.fill(documentName);
			await expect(name).toHaveValue(documentName);

			// A promoBanner in mainBlocks (modal) with an empty, hidden, mandatory anchorId in its Settings.
			let since = await pageNow(page);
			let modal = await addBlockFromCatalogue(page, 'mainBlocks', types.promoBanner.name);
			await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			await modal.property('heading').locator('input').first().fill('Promo with a hidden mandatory anchor');
			await modal.submit();
			await expect(blockEntry(page, 'mainBlocks', types.promoBanner.alias)).toHaveCount(1);

			// A promoBanner in inlineBlocks: its block workspace stays alive, so its element managers get the validation
			// messages as soon as they arrive.
			since = await pageNow(page);
			const inline = await addInlineBlockFromCatalogue(page, 'inlineBlocks', types.promoBanner.alias, types.promoBanner.name);
			await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
			const inlineEditor = await expandInlineBlock(inline);
			await inlineEditor.locator('umb-property[data-mark="property:heading"] input').first().fill('Inline promo with a hidden mandatory anchor');

			// Save and publish: Umbraco validates (metaTitle and both anchorIds are empty), saves the document and reports
			// that it could not be published.
			await expect(name).toHaveValue(documentName);
			await workspaceAction(page, 'SaveAndPublish').click();
			await expect(
				page.locator('uui-toast-notification').filter({ hasText: 'could not be published' }),
				"Umbraco's publish-blocked notification",
			).toBeVisible();
			await page.waitForURL(new RegExp(`/workspace/document/edit/${scaffoldKey}`));
			await settle(page);
			expect(rejectedValidations.length, 'server validations answered with 400').toBeGreaterThan(0);

			const document = await getDocument(page, scaffoldKey);
			expect(document.variants[0]?.state, 'the document was saved, not published').toBe('Draft');

			// Still hidden after the failed publish.
			await expectCorporateLandingPage(page);

			// The modal block: its content and settings element managers receive the block's validation messages when it
			// opens; the Settings view still shows cssClass and hides anchorId with its emptied group.
			since = await pageNow(page);
			modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
			await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			await modal.openView('settings');
			await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyHidden('anchorId');
			await expect(modal.group('Settings')).toHaveCount(0);
			await settle(page);
			await modal.close();
			await settle(page);

			expect(consoleErrors, 'console errors and uncaught page errors').toEqual([]);
		} finally {
			await setPropertyMandatory(page, keys.landingPageType, 'metaTitle', false);
			await setPropertyMandatory(page, types.promoBannerSettings.key, 'anchorId', false);
		}
	});
});
