import type { Page } from '@playwright/test';
import { backofficeFetch, managementApi } from './management-api.js';

/** The package's health check (HealthChecks/PropertyVisibilityHealthCheck.cs). */
export const HEALTH_CHECK_ID = 'b35b2a1d-1954-42f2-a2ec-73f7d2f366b4';
export const HEALTH_CHECK_NAME = 'Property Visibility configuration';
export const HEALTH_CHECK_GROUP = 'Configuration';

/** Umbraco's StatusResultType, by name (the Management API may serialise it as a name or as its number). */
export type ResultType = 'Success' | 'Warning' | 'Error' | 'Info';
const RESULT_TYPES: ResultType[] = ['Success', 'Warning', 'Error', 'Info'];

export interface HealthCheckResult {
	message: string;
	resultType: ResultType;
	readMoreLink?: string | null;
	/** The issue code of an issue status (`<strong>PVxxx</strong>` at the start of the message); none for the summary. */
	code?: string;
}

interface RawResult {
	message: string;
	resultType: string | number;
	readMoreLink?: string | null;
}

/** GET /umbraco/management/api/v1/health-check-group: the group names. */
export async function healthCheckGroupNames(page: Page): Promise<string[]> {
	const groups = await managementApi<{ total: number; items: Array<{ name: string }> }>(
		page,
		'/health-check-group?skip=0&take=100',
	);
	return groups.items.map((group) => group.name);
}

/** GET /umbraco/management/api/v1/health-check-group/{name}: the checks of one group. */
export async function healthCheckGroup(
	page: Page,
	name: string,
): Promise<{ name: string; checks: Array<{ id: string; name: string; description?: string | null }> }> {
	return managementApi(page, `/health-check-group/${encodeURIComponent(name)}`);
}

/**
 * POST /umbraco/management/api/v1/health-check-group/Configuration/check, the call the Health Check dashboard makes:
 * runs every check of the group and returns the results of the package's check (summary first, then the issues).
 */
export async function runPropertyVisibilityHealthCheck(page: Page): Promise<HealthCheckResult[]> {
	const { status, body } = await backofficeFetch<{ checks: Array<{ id: string; results?: RawResult[] | null }> }>(
		page,
		`/umbraco/management/api/v1/health-check-group/${HEALTH_CHECK_GROUP}/check`,
		{ method: 'POST' },
	);
	if (status !== 200) {
		throw new Error(`POST health-check-group/${HEALTH_CHECK_GROUP}/check returned ${status}: ${JSON.stringify(body)}`);
	}
	const check = body.checks.find((c) => c.id.toLowerCase() === HEALTH_CHECK_ID);
	if (!check) {
		throw new Error(`The ${HEALTH_CHECK_GROUP} group has no check ${HEALTH_CHECK_ID}: ${JSON.stringify(body)}`);
	}
	return (check.results ?? []).map((result) => ({
		message: result.message,
		resultType: typeof result.resultType === 'number' ? RESULT_TYPES[result.resultType] : (result.resultType as ResultType),
		readMoreLink: result.readMoreLink,
		code: /^<strong>(PV\d{3})<\/strong>/.exec(result.message)?.[1],
	}));
}

/** Runs the health check until the results satisfy the predicate (configuration reloads are asynchronous). */
export async function pollHealthCheck(
	page: Page,
	predicate: (results: HealthCheckResult[]) => boolean,
	description: string,
	timeout = 10_000,
): Promise<HealthCheckResult[]> {
	const deadline = Date.now() + timeout;
	for (;;) {
		const results = await runPropertyVisibilityHealthCheck(page);
		if (predicate(results)) return results;
		if (Date.now() >= deadline) {
			throw new Error(
				`The health check did not reach "${description}" within ${timeout} ms. Last results: ${JSON.stringify(results, null, 2)}`,
			);
		}
		await page.waitForTimeout(250);
	}
}

/** The issue codes in the results, in order. */
export function issueCodes(results: HealthCheckResult[]): string[] {
	return results.flatMap((result) => (result.code ? [result.code] : []));
}
