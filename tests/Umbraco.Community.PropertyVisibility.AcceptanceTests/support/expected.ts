import type { ContentTypeStructure } from './management-api.js';

export interface Rule {
	properties: string[];
	containers: string[];
}

/** The sample rules in the test site's appsettings.json that the document workspace and API specs rely on. */
export const RULES = {
	// Corporate's own landingPage entry united with the noBannerImage rule set it includes (bannerImage).
	corporateLandingPage: {
		properties: ['bannerImage', 'relatedLinks'],
		containers: ['seoTab', 'settingsTab/advanced'],
	},
	corporateArticle: { properties: [], containers: ['shareTab'] },
	campaignLandingPage: { properties: ['metaKeywords'], containers: [] },
} satisfies Record<string, Rule>;

const same = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;

/**
 * What the server should return for a content type under a rule, computed independently from the Management API's
 * view of the type: configured properties; configured containers plus the groups under a configured tab, with all
 * their properties; with HideEmptiedContainers, groups whose properties are now all hidden and tabs whose own
 * properties and child groups are all hidden.
 */
export function expectedHidden(structure: ContentTypeStructure, rule: Rule): { propertyKeys: string[]; containerKeys: string[] } {
	const hiddenContainers = new Set<string>();
	for (const container of structure.containers) {
		const parentAlias = container.alias.includes('/') ? container.alias.split('/')[0] : undefined;
		if (rule.containers.some((alias) => same(alias, container.alias) || (parentAlias && same(alias, parentAlias)))) {
			hiddenContainers.add(container.key);
		}
	}

	const hiddenProperties = new Set<string>();
	for (const property of structure.properties) {
		if (
			rule.properties.some((alias) => same(alias, property.alias)) ||
			(property.containerKey && hiddenContainers.has(property.containerKey))
		) {
			hiddenProperties.add(property.key);
		}
	}

	// Emptied containers, evaluated per merged container: the copies the type and its compositions hold render as one
	// container when they share the backoffice's merge key (type and encoded name, after those of the parent tab).
	const merged = new Map<string, typeof structure.containers>();
	for (const container of structure.containers) {
		const key = mergeKey(structure, container);
		merged.set(key, [...(merged.get(key) ?? []), container]);
	}
	const propertiesOf = (keys: Set<string>) => structure.properties.filter((p) => p.containerKey && keys.has(p.containerKey));

	for (const copies of merged.values()) {
		if (copies[0].type !== 'Group' || copies.every((c) => hiddenContainers.has(c.key))) continue;
		const props = propertiesOf(new Set(copies.map((c) => c.key)));
		if (props.length > 0 && props.every((p) => hiddenProperties.has(p.key))) {
			copies.forEach((c) => hiddenContainers.add(c.key));
		}
	}
	for (const copies of merged.values()) {
		if (copies[0].type !== 'Tab' || copies.every((c) => hiddenContainers.has(c.key))) continue;
		const tabKeys = new Set(copies.map((c) => c.key));
		const own = propertiesOf(tabKeys);
		const groups = structure.containers.filter((c) => c.parentKey && tabKeys.has(c.parentKey));
		if (groups.length === 0) continue;
		if (own.every((p) => hiddenProperties.has(p.key)) && groups.every((g) => hiddenContainers.has(g.key))) {
			copies.forEach((c) => hiddenContainers.add(c.key));
		}
	}

	return { propertyKeys: [...hiddenProperties].sort(), containerKeys: [...hiddenContainers].sort() };
}

/** The backoffice's encodeFolderName, which the content type structure manager uses to merge container copies. */
const encodeFolderName = (name: string) =>
	encodeURIComponent(name.toLowerCase().replace(/\s+/g, '-')).replace(/[_.!~*()]/g, '-').replace(/'/g, '');

/** The key under which the backoffice merges container copies (getContainerChainKey in content-type-structure-manager). */
function mergeKey(structure: ContentTypeStructure, container: ContentTypeStructure['containers'][number]): string {
	const own = `${container.type.toLowerCase()}/${encodeFolderName(container.name)}`;
	const parent = container.parentKey ? structure.containers.find((c) => c.key === container.parentKey) : undefined;
	return parent ? `${mergeKey(structure, parent)}|${own}` : own;
}

export function keysOfAliases(structure: ContentTypeStructure, aliases: string[]): string[] {
	return aliases.map((alias) => {
		const property = structure.properties.find((p) => p.alias === alias);
		if (!property) throw new Error(`Property ${alias} not found on ${structure.alias}`);
		return property.key;
	});
}

export function keyOfContainer(structure: ContentTypeStructure, alias: string): string {
	const container = structure.containers.find((c) => c.alias === alias);
	if (!container) {
		throw new Error(`Container ${alias} not found on ${structure.alias}: ${structure.containers.map((c) => c.alias).join(', ')}`);
	}
	return container.key;
}
