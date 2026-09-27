import {
	contentTab,
	expectPropertyHidden,
	expectPropertyVisible,
	openContentTab,
	propertyGroup,
	save,
	startCreateAtRoot,
	startCreateUnder,
	waitForAppliedResponse,
	waitForHiddenFields,
	waitForHiddenFieldsAtRoot,
	waitForHiddenFieldsWithParent,
} from '../support/backoffice.js';
import type { Response } from '@playwright/test';
import {
	addBlockFromCatalogue,
	blockEntry,
	openBlockModal,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import { keyOfContainer, keysOfAliases } from '../support/expected.js';
import { expectSiteLegacyTabKeepsOwnProperty } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import {
	compositionKey,
	deleteDocument,
	getContentTypeStructure,
	getDocument,
	getHiddenFields,
	resolveSample,
	setAllowedAsRoot,
	type HiddenFieldsResponse,
} from '../support/management-api.js';

const DEFAULT_SITE = { label: 'everythingElse', reason: 'Default' };

/** A hidden-fields response that was requested without a parent key, as JSON. */
async function withoutParent(response: Response): Promise<HiddenFieldsResponse> {
	expect(response.status()).toBe(200);
	expect(new URL(response.url()).searchParams.get('parentKey'), 'requested without parentKey').toBeNull();
	return (await response.json()) as HiddenFieldsResponse;
}

/**
 * A document created at the content root has no parent and no root yet: the backoffice requests its hidden fields
 * without `parentKey`, the root resolves to `None`, and the site matcher falls through to the `IsDefault` site
 * (`everythingElse` in the sample). After the first save the document is its own root, which no site claims by key or
 * name, so the default site still applies.
 *
 * The first test creates a `site` (the only type the seed allows at the root). The second one allows `landingPage` at
 * the root for its duration (Management API, set back in `finally`), so a document at the root gets a rule of its own
 * type applied (the default site's `bannerImage`) and a block in it requests its element types without a parent as
 * well.
 *
 * No rule is keyed by `site`, but the sample's top-level rule `ContentTypes.siteSettings` (`Containers: ["legacyTab"]`)
 * is keyed by a composition of `site`, and a rule keyed by a composition applies to every type composed of it
 * (docs/configuration.md, Compositions). So a new `site` document loses the composition's copy of the "Legacy tab",
 * its "General" group and both of its properties, before and after the first save, whatever site matches: the rule is
 * top-level. The keys are those of the `siteSettings` type itself, which the spec checks by asking the API for
 * `siteSettings`. The site type's own "Legacy tab" copy stays with `siteNotes` (spec 24 checks that its key is not in
 * the response).
 */
test.describe('Documents created at the content root', () => {
	test('a site at the root matches the default site before and after its first save and loses the composition\'s Legacy tab; a landingPage under it gets the default site rules', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const siteType = (await getDocument(page, keys.corporateSite)).documentType.id;
		const siteSettings = await getContentTypeStructure(page, await compositionKey(page, siteType, 'siteSettings'));
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);
		const legacyContainers = [keyOfContainer(siteSettings, 'legacyTab'), keyOfContainer(siteSettings, 'legacyTab/general')].sort();
		const legacyProperties = keysOfAliases(siteSettings, ['siteTitle', 'footerText']).sort();

		let rootKey: string | undefined;
		try {
			// The Create action of the Content tree's root, then "Site".
			const created = waitForHiddenFieldsAtRoot(page, siteType);
			await startCreateAtRoot(page, 'Site');
			const response = await created;
			expect(response.status()).toBe(200);
			const scaffoldKey = new URL(response.url()).searchParams.get('documentKey')!;
			const beforeSave = (await response.json()) as HiddenFieldsResponse;
			expect(beforeSave.rootResolution).toBe('None');
			expect(beforeSave.matchedSite).toEqual(DEFAULT_SITE);
			// The top-level siteSettings rule reaches the site type through its composition: the tab, its group, both properties.
			expect([...beforeSave.containerKeys].sort(), 'the composition rule reaches the site type').toEqual(legacyContainers);
			expect([...beforeSave.propertyTypeKeys].sort()).toEqual(legacyProperties);
			expect(beforeSave.warnings).toEqual([]);
			const applied = await waitForAppliedResponse(page, response, { target: 'document' });
			expect(applied.propertyCount).toBe(2);
			expect(applied.containerCount, 'the Legacy tab and its General group are no longer in the workspace structure').toBe(2);
			await expectSiteLegacyTabKeepsOwnProperty(page);

			// The same keys as for siteSettings itself: the rule applies to the composition's own type too.
			const composition = (await getHiddenFields(page, { documentKey: scaffoldKey, contentTypeKey: siteSettings.key })).body;
			expect(composition.matchedSite).toEqual(DEFAULT_SITE);
			expect([...composition.containerKeys].sort()).toEqual(legacyContainers);
			expect([...composition.propertyTypeKeys].sort()).toEqual(legacyProperties);

			// First save: the document is its own root now, and no site claims it by key or name.
			const rootName = `Acceptance root ${Date.now()}`;
			await page.locator('uui-input[data-mark="input:entity-name"] input').fill(rootName);
			const afterSave = waitForHiddenFields(page, scaffoldKey, { contentTypeKey: siteType });
			await save(page);
			rootKey = scaffoldKey;
			await page.waitForURL(new RegExp(`/workspace/document/edit/${scaffoldKey}`));
			const saved = await afterSave;
			expect(new URL(saved.url()).searchParams.get('parentKey')).toBeNull();
			const savedBody = (await saved.json()) as HiddenFieldsResponse;
			expect(savedBody.rootResolution).toBe('Document');
			expect(savedBody.matchedSite).toEqual(DEFAULT_SITE);
			expect([...savedBody.containerKeys].sort()).toEqual(legacyContainers);
			expect([...savedBody.propertyTypeKeys].sort()).toEqual(legacyProperties);
			expect(savedBody.warnings).toEqual([]);
			const reapplied = await waitForAppliedResponse(page, saved, { target: 'document' });
			expect(reapplied.containerCount).toBe(2);
			await expectSiteLegacyTabKeepsOwnProperty(page);

			// A landingPage under the new root: the default site's landingPage rule (bannerImage) and no top-level rule.
			// The Corporate and Campaign rules do not apply.
			const underRoot = waitForHiddenFieldsWithParent(page, rootKey);
			await startCreateUnder(page, rootName, SAMPLE.landingPageTypeName);
			const landing = await underRoot;
			expect(landing.status()).toBe(200);
			const landingKey = new URL(landing.url()).searchParams.get('documentKey')!;
			const landingBody = (await landing.json()) as HiddenFieldsResponse;
			expect(landingBody.rootResolution).toBe('Parent');
			expect(landingBody.matchedSite).toEqual(DEFAULT_SITE);
			expect(landingBody.propertyTypeKeys).toEqual(keysOfAliases(landingPage, ['bannerImage']));
			expect(landingBody.containerKeys).toEqual([]);
			await waitForAppliedResponse(page, landing, { target: 'document' });

			await openContentTab(page, 'Content');
			await expectPropertyVisible(page, 'title');
			await expectPropertyHidden(page, 'bannerImage');
			await expectPropertyVisible(page, 'relatedLinks');
			await expect(contentTab(page, 'Seo tab'), 'only the Corporate site hides the seoTab tab').toBeVisible();
			await openContentTab(page, 'Settings tab');
			await expect(propertyGroup(page, 'Advanced')).toBeVisible();
			await openContentTab(page, 'Content');

			// A promoBanner added to the unsaved landing page: only the Corporate site hides overlayColour and anchorId.
			const since = await pageNow(page);
			const modal = await addBlockFromCatalogue(page, 'mainBlocks', types.promoBanner.name);
			const content = await waitForBlockApplied(page, { documentKey: landingKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			expect(content.propertyCount).toBe(0);
			await modal.expectPropertyVisible('heading');
			await modal.expectPropertyVisible('overlayColour');
			await modal.openView('settings');
			const settings = await waitForBlockApplied(page, { documentKey: landingKey, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
			expect(settings.propertyCount).toBe(0);
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyVisible('anchorId');
			// Cancel: the block is discarded and the landing page is never saved.
			await modal.close();
		} finally {
			// The root and everything under it, so the next run starts from the seeded tree.
			if (rootKey) await deleteDocument(page, rootKey);
		}
	});

	test('a landingPage at the root: the default site hides bannerImage before and after the first save; a promoBanner in it requests without a parent and hides nothing', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);
		const bannerImage = keysOfAliases(landingPage, ['bannerImage']);

		let wasAllowedAsRoot: boolean | undefined;
		let documentKey: string | undefined;
		try {
			wasAllowedAsRoot = await setAllowedAsRoot(page, keys.landingPageType, true);
			expect(wasAllowedAsRoot, 'the seed does not allow landingPage at the root').toBe(false);

			// The Create action of the Content tree's root, then "Landing page": no parent, root resolution None.
			const created = waitForHiddenFieldsAtRoot(page, keys.landingPageType);
			await startCreateAtRoot(page, SAMPLE.landingPageTypeName);
			const response = await created;
			const scaffoldKey = new URL(response.url()).searchParams.get('documentKey')!;
			const beforeSave = await withoutParent(response);
			expect(beforeSave.rootResolution).toBe('None');
			expect(beforeSave.matchedSite).toEqual(DEFAULT_SITE);
			expect(beforeSave.propertyTypeKeys, "the default site's landingPage rule").toEqual(bannerImage);
			expect(beforeSave.containerKeys).toEqual([]);
			expect(beforeSave.warnings).toEqual([]);
			const applied = await waitForAppliedResponse(page, response, { target: 'document' });
			expect(applied.propertyCount).toBe(1);

			await openContentTab(page, 'Content');
			await expectPropertyVisible(page, 'title');
			await expectPropertyHidden(page, 'bannerImage');
			await expectPropertyVisible(page, 'relatedLinks');
			await expect(contentTab(page, 'Seo tab'), 'only the Corporate site hides the seoTab tab').toBeVisible();

			// The name first (spec 18: once a block modal has closed, fill() on the name did not always take).
			const name = page.locator('uui-input[data-mark="input:entity-name"] input');
			const documentName = `Acceptance root landing ${Date.now()}`;
			await name.fill(documentName);
			await expect(name).toHaveValue(documentName);

			// A promoBanner before the first save: both halves request under the scaffold key, without a parent.
			const contentRequest = waitForHiddenFields(page, scaffoldKey, { contentTypeKey: types.promoBanner.key });
			const settingsRequest = waitForHiddenFields(page, scaffoldKey, { contentTypeKey: types.promoBannerSettings.key });
			let since = await pageNow(page);
			const modal = await addBlockFromCatalogue(page, 'mainBlocks', types.promoBanner.name);
			for (const blockResponse of [await contentRequest, await settingsRequest]) {
				const body = await withoutParent(blockResponse);
				expect(body.rootResolution).toBe('None');
				expect(body.matchedSite).toEqual(DEFAULT_SITE);
				expect(body.propertyTypeKeys).toEqual([]);
				expect(body.containerKeys).toEqual([]);
			}
			const content = await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			expect(content.propertyCount).toBe(0);
			await modal.expectPropertyVisible('heading');
			await modal.expectPropertyVisible('overlayColour');
			await modal.openView('settings');
			const settings = await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
			expect(settings.propertyCount).toBe(0);
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyVisible('anchorId');
			await modal.openView('content');
			await modal.property('heading').locator('input').first().fill('Promo at the root');
			await modal.submit();
			await expect(blockEntry(page, 'mainBlocks', types.promoBanner.alias)).toHaveCount(1);

			// First save: the document is its own root now (Document), no site claims it, bannerImage stays hidden.
			await expect(name).toHaveValue(documentName);
			const afterSave = waitForHiddenFields(page, scaffoldKey, { contentTypeKey: keys.landingPageType });
			await save(page);
			documentKey = scaffoldKey;
			await page.waitForURL(new RegExp(`/workspace/document/edit/${scaffoldKey}`));
			const saved = await afterSave;
			const savedBody = await withoutParent(saved);
			expect(savedBody.rootResolution).toBe('Document');
			expect(savedBody.matchedSite).toEqual(DEFAULT_SITE);
			expect(savedBody.propertyTypeKeys).toEqual(bannerImage);
			expect(savedBody.containerKeys).toEqual([]);
			expect(savedBody.warnings).toEqual([]);
			await waitForAppliedResponse(page, saved, { target: 'document' });
			await openContentTab(page, 'Content');
			await expectPropertyVisible(page, 'title');
			await expectPropertyHidden(page, 'bannerImage');

			// The saved promoBanner, reopened: requested again for the saved document (Document), still nothing hidden.
			const reopenRequest = waitForHiddenFields(page, scaffoldKey, { contentTypeKey: types.promoBanner.key });
			since = await pageNow(page);
			const reopened = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
			const reopenBody = await withoutParent(await reopenRequest);
			expect(reopenBody.rootResolution).toBe('Document');
			expect(reopenBody.matchedSite).toEqual(DEFAULT_SITE);
			expect(reopenBody.propertyTypeKeys).toEqual([]);
			const reapplied = await waitForBlockApplied(page, { documentKey: scaffoldKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			expect(reapplied.propertyCount).toBe(0);
			await reopened.expectPropertyVisible('heading');
			await reopened.expectPropertyVisible('overlayColour');
			await reopened.close();
		} finally {
			if (documentKey) await deleteDocument(page, documentKey);
			if (wasAllowedAsRoot !== undefined) await setAllowedAsRoot(page, keys.landingPageType, wasAllowedAsRoot);
		}
	});
});
