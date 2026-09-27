import { expectedHidden, keyOfContainer, keysOfAliases, RULES } from '../support/expected.js';
import { expect, test } from '../support/fixtures.js';
import {
	HEALTH_CHECK_GROUP,
	HEALTH_CHECK_ID,
	HEALTH_CHECK_NAME,
	healthCheckGroup,
	healthCheckGroupNames,
	issueCodes,
	pollHealthCheck,
	type HealthCheckResult,
} from '../support/health-check.js';
import { getContentTypeStructure, managementApi, resolveSample } from '../support/management-api.js';
import { hidesExactly, pollHiddenFields, TemporaryRulesFile } from '../support/site-config.js';

// Changes the running site's rules source: no other test may run while the rules file exists.
test.describe.configure({ mode: 'serial' });

const RELOAD_TIMEOUT_MS = 5_000;

/**
 * A Success summary with nothing but informational statuses after it. On an Umbraco version outside the tested list
 * (the compatibility job builds against the latest 17.x) the check adds PV303 at Info, and the summary stays Success.
 */
const isHealthy = (results: HealthCheckResult[]) =>
	results[0]?.resultType === 'Success' && results.every((result) => result.resultType !== 'Error' && result.resultType !== 'Warning');

/** The issue codes of the results, without the version notice PV303 (present on an untested Umbraco version only). */
const configurationIssueCodes = (results: HealthCheckResult[]) => issueCodes(results).filter((code) => code !== 'PV303');

function summaryOf(results: HealthCheckResult[]): HealthCheckResult {
	expect(results[0]?.code, 'the first status is the summary').toBeUndefined();
	return results[0];
}

function statusFor(results: HealthCheckResult[], code: string): HealthCheckResult {
	const matching = results.filter((result) => result.code === code);
	expect(matching, `one ${code} status in ${JSON.stringify(results, null, 2)}`).toHaveLength(1);
	return matching[0];
}

test.describe('The Property Visibility health check', () => {
	test('is listed in the Configuration group of the Management API', async ({ page }) => {
		expect(await healthCheckGroupNames(page)).toContain(HEALTH_CHECK_GROUP);

		const group = await healthCheckGroup(page, HEALTH_CHECK_GROUP);
		expect(group.checks).toEqual(
			expect.arrayContaining([expect.objectContaining({ id: HEALTH_CHECK_ID, name: HEALTH_CHECK_NAME })]),
		);
	});

	test('reports Success on the sample configuration', async ({ page }) => {
		const results = await pollHealthCheck(page, isHealthy, 'Success', RELOAD_TIMEOUT_MS);
		const summary = summaryOf(results);
		expect(summary.resultType).toBe('Success');
		expect(summary.message).toContain('Rules source: appsettings');
		expect(summary.message).toMatch(/rules hash <code>[0-9a-f]{64}<\/code>/);
	});

	test('a rules file with an unknown property alias, a non-root RootNodeKey and a hidden mandatory property reports PV201, PV101, PV203 and PV301; invalid JSON reports PV001 and keeps the last valid rules; deleting the file is Success again', async ({ page }) => {
		const keys = await resolveSample(page);
		const landingPage = await getContentTypeStructure(page, keys.landingPageType);
		const [trackingId] = keysOfAliases(landingPage, ['trackingId']);
		const generalGroup = keyOfContainer(landingPage, 'settingsTab/general');
		const query = { documentKey: keys.corporateLanding, contentTypeKey: keys.landingPageType };

		// The uSync seed makes article.title mandatory (the clean-database import is what put it there).
		const article = await managementApi<{ properties: Array<{ alias: string; validation: { mandatory: boolean } }> }>(
			page,
			`/document-type/${keys.articleType}`,
		);
		expect(article.properties.find((property) => property.alias === 'title')?.validation.mandatory).toBe(true);

		await pollHealthCheck(page, isHealthy, 'Success before the rules file', RELOAD_TIMEOUT_MS);

		const rules = {
			Sites: {
				corporate: {
					RootNodeKey: keys.corporateSite,
					ContentTypes: {
						// (a) bannerImg does not exist on landingPage (PV201); trackingId does, so the rules are observable.
						landingPage: { Properties: ['bannerImg', 'trackingId'] },
						// (c) article.title is mandatory (PV203).
						article: { Properties: ['title'] },
					},
				},
				campaign: {
					// (b) a document below a root, not a root (PV101); the site still matches its root by name.
					RootNodeKey: keys.corporateLanding,
					RootNodeName: 'Campaign site',
					ContentTypes: { landingPage: { Properties: ['metaKeywords'] } },
				},
			},
		};

		const rulesFile = TemporaryRulesFile.begin();
		try {
			rulesFile.write(rules);
			await pollHiddenFields(
				page,
				query,
				(body) => body.propertyTypeKeys.includes(trackingId),
				'the rules file is loaded',
				RELOAD_TIMEOUT_MS,
			);

			const withIssues = await pollHealthCheck(
				page,
				(results) => ['PV101', 'PV201', 'PV203', 'PV301'].every((code) => issueCodes(results).includes(code)),
				'PV101, PV201, PV203 and PV301',
				RELOAD_TIMEOUT_MS,
			);
			expect(configurationIssueCodes(withIssues).sort()).toEqual(['PV101', 'PV201', 'PV203', 'PV301']);
			const summary = summaryOf(withIssues);
			expect(summary.resultType).toBe('Info');
			expect(summary.message).toContain('0 errors and 4 warnings');
			expect(summary.message).toContain('Rules source: the rules file');

			const pv201 = statusFor(withIssues, 'PV201');
			expect(pv201.resultType).toBe('Warning');
			expect(pv201.message).toContain('Sites:corporate:ContentTypes:landingPage:Properties');
			expect(pv201.message).toContain('bannerImg');
			expect(pv201.message).toContain('Did you mean <code>bannerImage</code>?');

			const pv101 = statusFor(withIssues, 'PV101');
			expect(pv101.resultType).toBe('Warning');
			expect(pv101.message).toContain('Sites:campaign');
			expect(pv101.message).toContain(keys.corporateLanding);
			// The suggestion is the key of the root the site's RootNodeName matches.
			expect(pv101.message).toContain(`Did you mean <code>${keys.campaignSite}</code>?`);

			const pv203 = statusFor(withIssues, 'PV203');
			expect(pv203.resultType).toBe('Warning');
			expect(pv203.message).toContain('Sites:corporate:ContentTypes:article');
			expect(pv203.message).toContain('title');

			const pv301 = statusFor(withIssues, 'PV301');
			expect(pv301.resultType).toBe('Warning');

			for (const status of withIssues.slice(1)) {
				expect(status.readMoreLink).toMatch(/docs\/configuration\.md#issue-codes$/);
			}

			// Invalid JSON (a missing comma): PV001, and the last valid version of the file stays in effect.
			rulesFile.write('{\n\t"Sites": {\n\t\t"corporate": { "RootNodeKey": "' + keys.corporateSite + '" }\n\t\t"campaign": {}\n\t}\n}\n');
			const broken = await pollHealthCheck(
				page,
				(results) => issueCodes(results).includes('PV001'),
				'PV001',
				RELOAD_TIMEOUT_MS,
			);
			const pv001 = statusFor(broken, 'PV001');
			expect(pv001.resultType).toBe('Error');
			expect(pv001.message).toMatch(/line 4, position \d+/);
			// The previous rules still apply: the API returns the keys of the last valid file.
			const lastValid = await pollHiddenFields(page, query, () => true, 'any response', RELOAD_TIMEOUT_MS);
			expect(lastValid.propertyTypeKeys).toEqual([trackingId]);
			expect(lastValid.containerKeys).toEqual([generalGroup]);
		} finally {
			rulesFile.restore();
		}

		const appSettingsRules = expectedHidden(landingPage, RULES.corporateLandingPage);
		await pollHiddenFields(page, query, (body) => hidesExactly(body, appSettingsRules), 'the appsettings rules again', RELOAD_TIMEOUT_MS);
		const healthyAgain = await pollHealthCheck(page, isHealthy, 'Success after deleting the rules file', RELOAD_TIMEOUT_MS);
		expect(summaryOf(healthyAgain).message).toContain('Rules source: appsettings');
	});
});
