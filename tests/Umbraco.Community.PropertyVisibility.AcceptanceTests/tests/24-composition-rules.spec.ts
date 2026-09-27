import { waitForAppliedResponse, waitForHiddenFields } from '../support/backoffice.js';
import { SAMPLE } from '../support/env.js';
import { keyOfContainer, keysOfAliases } from '../support/expected.js';
import { expectSiteLegacyTabKeepsOwnProperty } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import {
	compositionKey,
	getContentTypeStructure,
	getDocument,
	getHiddenFields,
	resolveSample,
	type HiddenFieldsResponse,
} from '../support/management-api.js';

/**
 * Rules keyed by a composition (docs/configuration.md, "Compositions"): a rule keyed by type X applies to X and to every
 * type composed of X, and resolves against X's own structure. The sample's top-level rule
 * `siteSettings: { Containers: ["legacyTab"] }` is keyed by a composition of the `site` document type, so it reaches the
 * seeded site roots: "Corporate site" (matched by RootNodeKey) and "Campaign site" (matched by RootNodeName).
 *
 * The `site` type has a tab of its own with the same name and alias as the composition's tab ("Legacy tab", holding
 * `siteNotes`); the backoffice shows the two copies as one tab. The response carries only the keys of the `siteSettings`
 * type (its Legacy tab, its General group, siteTitle and footerText), never the site type's own tab, so the Legacy tab
 * stays in the editor with `siteNotes`, while the General group and both of its properties are gone.
 */
test.describe('Rules keyed by a composition', () => {
	const sites = [
		{ name: SAMPLE.corporateSite, key: 'corporateSite', matchedSite: { label: 'corporate', reason: 'Key' } },
		{ name: SAMPLE.campaignSite, key: 'campaignSite', matchedSite: { label: 'campaign', reason: 'Name' } },
	] as const;

	for (const site of sites) {
		test(`the top-level siteSettings rule removes the composition's Legacy tab copy from the existing "${site.name}" document and the site type's own Legacy tab stays`, async ({ page }) => {
			const keys = await resolveSample(page);
			const documentKey = keys[site.key];
			const siteType = (await getDocument(page, documentKey)).documentType.id;
			const siteSettings = await getContentTypeStructure(page, await compositionKey(page, siteType, 'siteSettings'));
			const legacyContainers = [keyOfContainer(siteSettings, 'legacyTab'), keyOfContainer(siteSettings, 'legacyTab/general')].sort();
			const legacyProperties = keysOfAliases(siteSettings, ['siteTitle', 'footerText']).sort();
			const siteStructure = await getContentTypeStructure(page, siteType);
			const ownLegacyTab = siteStructure.containers.find((c) => c.alias === 'legacyTab' && c.contentTypeAlias === 'site');
			expect(ownLegacyTab, 'precondition: the site type has a Legacy tab of its own').toBeDefined();
			expect(ownLegacyTab!.name, "precondition: with the same name as the composition's tab").toBe('Legacy tab');
			const siteNotes = keysOfAliases(siteStructure, ['siteNotes'])[0];

			const response = waitForHiddenFields(page, documentKey, { contentTypeKey: siteType });
			await page.goto(`/umbraco/section/content/workspace/document/edit/${documentKey}`);
			const result = await response;
			expect(result.status()).toBe(200);
			expect(new URL(result.url()).searchParams.get('parentKey'), 'an existing document is requested without a parent').toBeNull();
			const body = (await result.json()) as HiddenFieldsResponse;
			expect(body.rootResolution).toBe('Document');
			expect(body.matchedSite).toEqual(site.matchedSite);
			expect([...body.containerKeys].sort(), 'the composition rule reaches the site type').toEqual(legacyContainers);
			expect([...body.propertyTypeKeys].sort()).toEqual(legacyProperties);
			expect(body.containerKeys, "the site type's own Legacy tab is not in the response").not.toContain(ownLegacyTab!.key);
			expect(body.propertyTypeKeys).not.toContain(siteNotes);
			expect(body.warnings).toEqual([]);

			const applied = await waitForAppliedResponse(page, result, { target: 'document' });
			expect(applied.propertyCount).toBe(2);
			expect(applied.containerCount, 'the Legacy tab and its General group are no longer in the workspace structure').toBe(2);
			await expectSiteLegacyTabKeepsOwnProperty(page);

			// The rule keyed by siteSettings resolves against siteSettings: the same keys as for the composition itself.
			const composition = (await getHiddenFields(page, { documentKey, contentTypeKey: siteSettings.key })).body;
			expect([...composition.containerKeys].sort()).toEqual(legacyContainers);
			expect([...composition.propertyTypeKeys].sort()).toEqual(legacyProperties);
		});
	}
});
