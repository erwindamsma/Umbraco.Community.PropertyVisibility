import type { HiddenFieldsResponseModel } from '../api/index.js';
import {
	UmbPropertyVisibilityHiddenFieldsServerDataSource,
	type UmbPropertyVisibilityHiddenFieldsRequestArgs,
} from './hidden-fields.server.data-source.js';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbRepositoryBase, type UmbDataSourceResponse } from '@umbraco-cms/backoffice/repository';

/** Repository in front of the hidden-fields data source; memoisation lives in the site context, not here. */
export class UmbPropertyVisibilityHiddenFieldsRepository extends UmbRepositoryBase {
	#dataSource: UmbPropertyVisibilityHiddenFieldsServerDataSource;

	constructor(host: UmbControllerHost) {
		super(host);
		this.#dataSource = new UmbPropertyVisibilityHiddenFieldsServerDataSource(this);
	}

	requestHiddenFields(
		args: UmbPropertyVisibilityHiddenFieldsRequestArgs,
	): Promise<UmbDataSourceResponse<HiddenFieldsResponseModel>> {
		return this.#dataSource.getHiddenFields(args);
	}
}
