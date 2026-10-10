import { existsSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '../support/fixtures.js';
import {
	createDocument,
	deleteDocument,
	getDocument,
	renameDocument,
	resolveSample,
	setPropertyMandatory,
} from '../support/management-api.js';
import { SITE_CONTENT_ROOT, TemporaryRulesFile } from '../support/site-config.js';

// Changes the running site's rules source: no other test may run while the rules file exists.
test.describe.configure({ mode: 'serial' });

const SCHEMA_FILE = join(SITE_CONTENT_ROOT, 'PropertyVisibility.config-schema.local.json');

/** The rules file is picked up 250 ms after it is written; the writer merges requests for a second. */
const WRITE_TIMEOUT_MS = 10_000;

interface AliasList {
	items: { enum?: string[]; enumDescriptions?: string[] };
}

interface Schema {
	properties: { ContentTypes: { properties: Record<string, unknown> } };
	definitions: Record<
		string,
		{ properties: { Properties: AliasList; Containers: AliasList; RootNodeKey: { enum?: string[] }; RootNodeName: { enum?: string[] } } }
	>;
}

/** The schema as the site last wrote it; undefined while there is none. */
function readSchema(): Schema | undefined {
	return existsSync(SCHEMA_FILE) ? (JSON.parse(readFileSync(SCHEMA_FILE, 'utf8')) as Schema) : undefined;
}

/** Polls the schema file until it satisfies the predicate, and returns that schema. */
async function pollSchema(predicate: (schema: Schema) => boolean, description: string): Promise<Schema> {
	await expect
		.poll(() => {
			const schema = readSchema();
			return schema !== undefined && predicate(schema);
		}, { message: description, timeout: WRITE_TIMEOUT_MS })
		.toBe(true);
	return readSchema()!;
}

/**
 * Runs `body` while a rules file pointing at the schema exists and the site has written the schema for it. Deletes the
 * schema first, so the first poll sees a new write (spec 10's rules file also made the site write one), and again at
 * the end: without a rules file the site no longer writes it, so a copy left behind would go stale.
 */
async function withRulesFile(body: () => Promise<void>): Promise<void> {
	rmSync(SCHEMA_FILE, { force: true });
	const rulesFile = TemporaryRulesFile.begin();
	try {
		rulesFile.write({ $schema: './PropertyVisibility.config-schema.local.json' });
		await pollSchema(() => true, 'the site writes the schema for the new rules file');
		await body();
	} finally {
		rulesFile.restore();
		rmSync(SCHEMA_FILE, { force: true });
	}
}

function rootKeys(schema: Schema): string[] {
	return schema.definitions.SiteVisibilityOptions.properties.RootNodeKey.enum ?? [];
}

function rootNames(schema: Schema): string[] {
	return schema.definitions.SiteVisibilityOptions.properties.RootNodeName.enum ?? [];
}

/** The description the schema gives a property alias of a content type. */
function propertyDescription(schema: Schema, contentType: string, alias: string): string | undefined {
	const items = schema.definitions[`ContentType.${contentType}`].properties.Properties.items;
	const index = items.enum?.indexOf(alias) ?? -1;
	return index < 0 ? undefined : items.enumDescriptions?.[index];
}

test.describe('The alias schema next to the rules file (Development)', () => {
	test('creating a rules file makes the site write a schema with its aliases and root nodes', async ({ page }) => {
		const keys = await resolveSample(page);
		await withRulesFile(async () => {
			const schema = readSchema()!;

			expect(Object.keys(schema.properties.ContentTypes.properties)).toEqual(
				expect.arrayContaining(['landingPage', 'article', 'promoBanner', 'promoBannerSettings', 'siteSettings']),
			);
			const landingPage = schema.definitions['ContentType.landingPage'].properties;
			expect(landingPage.Properties.items.enum).toEqual(expect.arrayContaining(['bannerImage', 'relatedLinks', 'metaKeywords']));
			expect(landingPage.Containers.items.enum).toEqual(expect.arrayContaining(['seoTab', 'seoTab/meta', 'settingsTab/advanced']));
			expect(rootKeys(schema)).toEqual(expect.arrayContaining([keys.corporateSite, keys.campaignSite]));
		});
	});

	test('saving a document type rewrites the schema', async ({ page }) => {
		const keys = await resolveSample(page);
		const mandatory = (schema: Schema) => propertyDescription(schema, 'landingPage', 'metaKeywords')?.includes('mandatory') === true;
		await withRulesFile(async () => {
			expect(mandatory(readSchema()!)).toBe(false);
			try {
				await setPropertyMandatory(page, keys.landingPageType, 'metaKeywords', true);
				await pollSchema(mandatory, 'metaKeywords is described as mandatory after the document type was saved');
			} finally {
				await setPropertyMandatory(page, keys.landingPageType, 'metaKeywords', false);
			}
			await pollSchema((schema) => !mandatory(schema), 'metaKeywords is no longer described as mandatory');
		});
	});

	test('creating, renaming and deleting a root node rewrites the schema', async ({ page }) => {
		const keys = await resolveSample(page);
		const siteType = (await getDocument(page, keys.corporateSite)).documentType.id;
		const name = `Acceptance schema root ${Date.now()}`;
		const renamed = `${name} renamed`;
		await withRulesFile(async () => {
			let rootKey: string | undefined;
			try {
				rootKey = await createDocument(page, siteType, name);
				const created = rootKey;
				await pollSchema(
					(schema) => rootKeys(schema).includes(created) && rootNames(schema).includes(name),
					'the new root is listed by key and by name',
				);

				await renameDocument(page, created, renamed);
				await pollSchema(
					(schema) => rootNames(schema).includes(renamed) && !rootNames(schema).includes(name),
					'the renamed root is listed by its new name only',
				);

				await deleteDocument(page, created);
				// Deleting it again would log the API's 404 as a console error (spec 08).
				rootKey = undefined;
				await pollSchema(
					(schema) => !rootKeys(schema).includes(created) && !rootNames(schema).includes(renamed),
					'the deleted root is no longer listed',
				);
				expect(rootKeys(readSchema()!)).toEqual(expect.arrayContaining([keys.corporateSite, keys.campaignSite]));
			} finally {
				if (rootKey) await deleteDocument(page, rootKey);
			}
		});
	});
});
