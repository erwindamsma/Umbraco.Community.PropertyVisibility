import { PropertyVisibilityService, type HiddenFieldsResponseModel } from '../api/index.js';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbDataSourceResponse } from '@umbraco-cms/backoffice/repository';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

export interface UmbPropertyVisibilityHiddenFieldsRequestArgs {
	/** The document being edited; for a new document the client-generated scaffold key. */
	documentKey: string;
	/** The document type or element type whose hidden fields are requested. */
	contentTypeKey: string;
	/** The parent of a not-yet-saved document; omitted for existing documents and for a document created at the root. */
	parentKey?: string | null;
}

/**
 * Server data source for GET /umbraco/property-visibility/v1/hiddenfields.
 * This is the only call site of the generated SDK (src/api): after `npm run generate-client` regenerates it
 * from the running test site, only the service method name below may need to follow.
 *
 * Umbraco's own error notification is off for this request: it would show a generic "An error occurred" (or
 * "Connection lost") for every content type of every document and block opened while the API fails. The site context
 * reports a failure instead, once per browser session and attributed to this package.
 */
export class UmbPropertyVisibilityHiddenFieldsServerDataSource {
	#host: UmbControllerHost;

	constructor(host: UmbControllerHost) {
		this.#host = host;
	}

	async getHiddenFields(
		args: UmbPropertyVisibilityHiddenFieldsRequestArgs,
	): Promise<UmbDataSourceResponse<HiddenFieldsResponseModel>> {
		const { data, error } = await tryExecute(
			this.#host,
			PropertyVisibilityService.getUmbracoPropertyVisibilityV1HiddenFields({
				query: {
					documentKey: args.documentKey,
					contentTypeKey: args.contentTypeKey,
					parentKey: args.parentKey ?? undefined,
				},
			}),
			{ disableNotifications: true },
		);

		return { data, error };
	}
}
