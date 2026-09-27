import {
	contentTab,
	expectPropertyVisible,
	openContentTab,
	openDocument,
	propertyGroup,
	propertySlot,
} from '../support/backoffice.js';
import { expectedHidden, keyOfContainer, keysOfAliases, RULES } from '../support/expected.js';
import { expectCorporateLandingPage } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getContentTypeStructure, resolveSample } from '../support/management-api.js';
import { hidesExactly, pollHiddenFields, TemporaryRulesFile } from '../support/site-config.js';

// Changes the running site's rules source: no other test may run while the rules file exists.
test.describe.configure({ mode: 'serial' });

/** The rules file replaces the appsettings rules within this long (250 ms debounce plus the watcher). */
const RELOAD_TIMEOUT_MS = 5_000;

test.describe('Editing the rules file changes behaviour without a restart', () => {
	test('a rules file hiding trackingId on the Corporate landingPage replaces the appsettings rules and empties settingsTab/general; deleting it brings the appsettings rules back', async ({ page }) => {
		const keys = await resolveSample(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);
		const [trackingId] = keysOfAliases(landingPage, ['trackingId']);
		const generalGroup = keyOfContainer(landingPage, 'settingsTab/general');
		const query = { documentKey: keys.corporateLanding, contentTypeKey: keys.landingPageType };
		const appSettingsRules = expectedHidden(landingPage, RULES.corporateLandingPage);

		// Before: appsettings is the active source.
		await pollHiddenFields(page, query, (body) => hidesExactly(body, appSettingsRules), 'the appsettings rules', RELOAD_TIMEOUT_MS);

		const rulesFile = TemporaryRulesFile.begin();
		try {
			rulesFile.write({
				$schema: './PropertyVisibility.config-schema.json',
				Sites: {
					corporate: {
						RootNodeKey: keys.corporateSite,
						ContentTypes: { landingPage: { Properties: ['trackingId'] } },
					},
				},
			});

			const fromFile = await pollHiddenFields(
				page,
				query,
				(body) => body.propertyTypeKeys.includes(trackingId),
				'trackingId hidden by the rules file',
				RELOAD_TIMEOUT_MS,
			);
			// Replace, not merge: only the file's rule applies. trackingId is the only property of settingsTab/general, so
			// HideEmptiedContainers (default true) hides that group as well.
			expect(fromFile.propertyTypeKeys).toEqual([trackingId]);
			expect(fromFile.containerKeys).toEqual([generalGroup]);
			expect(fromFile.matchedSite).toEqual({ label: 'corporate', reason: 'Key' });
			expect(fromFile.disabled).toBe(false);

			await openDocument(page, keys.corporateLanding);
			await openContentTab(page, 'Content');
			await expectPropertyVisible(page, 'bannerImage');
			await expectPropertyVisible(page, 'relatedLinks');
			await expect(contentTab(page, 'Seo tab'), 'the appsettings rule for seoTab no longer applies').toBeVisible();

			await openContentTab(page, 'Settings tab');
			await expect(propertyGroup(page, 'General'), 'the emptied settingsTab/general group').toHaveCount(0);
			await expect(propertySlot(page, 'trackingId')).toHaveCount(0);
			await expectPropertyVisible(page, 'hideFromNavigation');
			await expect(propertyGroup(page, 'Advanced')).toBeVisible();
			await expectPropertyVisible(page, 'legacyRedirect');
		} finally {
			rulesFile.restore();
		}

		// After deleting the file, without a restart: the appsettings rules again.
		await pollHiddenFields(page, query, (body) => hidesExactly(body, appSettingsRules), 'the appsettings rules again', RELOAD_TIMEOUT_MS);
		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);
	});
});
