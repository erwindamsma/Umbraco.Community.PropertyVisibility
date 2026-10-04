import { existsSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';
import { SITE_CONTENT_ROOT, TemporaryRulesFile } from '../support/site-config.js';

// Changes the running site's rules source: no other test may run while the rules file exists.
test.describe.configure({ mode: 'serial' });

const SCHEMA_FILE = join(SITE_CONTENT_ROOT, 'PropertyVisibility.config-schema.local.json');

/** The rules file is picked up 250 ms after it is written; the writer merges requests for a second. */
const WRITE_TIMEOUT_MS = 10_000;

interface AliasList {
	items: { enum?: string[] };
}

interface Schema {
	properties: { ContentTypes: { properties: Record<string, unknown> } };
	definitions: Record<string, { properties: { Properties: AliasList; Containers: AliasList; RootNodeKey: { enum?: string[] } } }>;
}

test.describe('The alias schema next to the rules file (Development)', () => {
	test('creating a rules file makes the site write a schema with its aliases and root nodes', async ({ page }) => {
		const keys = await resolveSample(page);
		// A generated file, also written while spec 10's rules file existed: delete it, so the poll below sees a new write.
		rmSync(SCHEMA_FILE, { force: true });
		const rulesFile = TemporaryRulesFile.begin();
		try {
			rulesFile.write({ $schema: './PropertyVisibility.config-schema.local.json' });

			await expect.poll(() => existsSync(SCHEMA_FILE), { timeout: WRITE_TIMEOUT_MS }).toBe(true);
			const schema = JSON.parse(readFileSync(SCHEMA_FILE, 'utf8')) as Schema;

			expect(Object.keys(schema.properties.ContentTypes.properties)).toEqual(
				expect.arrayContaining(['landingPage', 'article', 'promoBanner', 'promoBannerSettings', 'siteSettings']),
			);
			const landingPage = schema.definitions['ContentType.landingPage'].properties;
			expect(landingPage.Properties.items.enum).toEqual(expect.arrayContaining(['bannerImage', 'relatedLinks', 'metaKeywords']));
			expect(landingPage.Containers.items.enum).toEqual(expect.arrayContaining(['seoTab', 'seoTab/meta', 'settingsTab/advanced']));
			expect(schema.definitions.SiteVisibilityOptions.properties.RootNodeKey.enum).toEqual(
				expect.arrayContaining([keys.corporateSite, keys.campaignSite]),
			);
		} finally {
			rulesFile.restore();
			// Without a rules file the site no longer writes it; do not leave a stale copy behind.
			rmSync(SCHEMA_FILE, { force: true });
		}
	});
});
