import {
	contentTab,
	expectPropertyVisible,
	openContentTab,
	openDocument,
	propertyGroup,
} from '../support/backoffice.js';
import { expectedHidden, RULES } from '../support/expected.js';
import { expectCorporateLandingPage } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getContentTypeStructure, resolveSample } from '../support/management-api.js';
import {
	hidesExactly,
	pollHiddenFields,
	TemporaryAppSettings,
	withPropertyVisibilityEnabled,
} from '../support/site-config.js';

// Changes the running site's appsettings.json: no other test may run while it is changed.
test.describe.configure({ mode: 'serial' });

/** The host reloads appsettings.json on change; the options monitor rebuilds right after. */
const RELOAD_TIMEOUT_MS = 10_000;

test.describe('Editing appsettings.json changes behaviour without a restart', () => {
	test('PropertyVisibility:Enabled false shows the Corporate landing page in full; restoring the file hides again', async ({ page }) => {
		const keys = await resolveSample(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);
		const query = { documentKey: keys.corporateLanding, contentTypeKey: keys.landingPageType };
		const appSettingsRules = expectedHidden(landingPage, RULES.corporateLandingPage);

		await pollHiddenFields(
			page,
			query,
			(body) => !body.disabled && hidesExactly(body, appSettingsRules),
			'enabled, with the appsettings rules',
			RELOAD_TIMEOUT_MS,
		);

		const appSettings = TemporaryAppSettings.begin();
		try {
			appSettings.write(withPropertyVisibilityEnabled(appSettings.originalText, false));

			const disabled = await pollHiddenFields(page, query, (body) => body.disabled, 'disabled', RELOAD_TIMEOUT_MS);
			expect(disabled.propertyTypeKeys).toEqual([]);
			expect(disabled.containerKeys).toEqual([]);
			expect(disabled.warnings).toEqual([expect.stringMatching(/^PV302\b/)]);

			await openDocument(page, keys.corporateLanding);
			await openContentTab(page, 'Content');
			await expectPropertyVisible(page, 'bannerImage');
			await expectPropertyVisible(page, 'relatedLinks');

			await expect(contentTab(page, 'Seo tab')).toBeVisible();
			await openContentTab(page, 'Seo tab');
			await expect(propertyGroup(page, 'Meta')).toBeVisible();
			await expectPropertyVisible(page, 'metaTitle');
			await expectPropertyVisible(page, 'metaKeywords');

			await openContentTab(page, 'Settings tab');
			await expect(propertyGroup(page, 'Advanced')).toBeVisible();
			await expectPropertyVisible(page, 'legacyRedirect');
			await expectPropertyVisible(page, 'enableExperimental');
		} finally {
			// Byte for byte, verified by restore().
			appSettings.restore();
		}

		await pollHiddenFields(
			page,
			query,
			(body) => !body.disabled && hidesExactly(body, appSettingsRules),
			'enabled again, with the appsettings rules',
			RELOAD_TIMEOUT_MS,
		);
		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);
	});
});
