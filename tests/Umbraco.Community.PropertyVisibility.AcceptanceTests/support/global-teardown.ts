import { restoreSchemaChanges } from './schema-changes.js';
import { restoreSiteConfiguration } from './site-config.js';

/**
 * Runs once after the suite, also when it was interrupted (Playwright runs the global teardown on the first Ctrl+C):
 * restores appsettings.json from the copy taken at the start, deletes a rules file the suite wrote and removes the
 * schema changes of specs 30 to 35, when a test body did not get to its own `finally`. A hard kill skips this too; the
 * next run's global setup then restores.
 */
export default async function globalTeardown(): Promise<void> {
	for (const action of [...restoreSiteConfiguration(), ...(await restoreSchemaChanges())]) {
		console.warn(`[acceptance teardown] ${action}.`);
	}
}
