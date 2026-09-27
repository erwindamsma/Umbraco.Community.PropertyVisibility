import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

/**
 * Every text the package shows in the backoffice. Keys are used as `propertyVisibility_<key>` (for example
 * `propertyVisibility_containerHidingUnavailable`); English only, which Umbraco falls back to for every culture.
 */
export default {
	propertyVisibility: {
		packageName: 'Property Visibility',
		containerHidingUnavailable:
			'Property Visibility cannot remove tabs and groups in this Umbraco version. Their properties are still hidden, but the empty tabs and groups stay visible. The browser console has the details.',
		hiddenFieldsRequestFailed:
			'Property Visibility could not load its rules, so all properties are shown. It tries again for the next document or block you open, without repeating this message. The browser console has the details.',
	},
} satisfies UmbLocalizationDictionary;
