import { restoreSchemaChanges } from './schema-changes.js';
import { prepareSiteConfiguration, restoreSiteConfiguration } from './site-config.js';

/**
 * Runs once before the suite. Specs 10 to 12, 25 and 35 change the running site's appsettings.json and rules file, and
 * specs 30 to 35 its document types, member type and data types; they restore them in `finally` blocks, which an
 * interrupted run (Ctrl+C, a cancelled CI job, a crashed worker) skips. So first undo what an earlier run left behind,
 * then fail fast unless the site is in the state the suite expects, and keep a copy of appsettings.json for the global
 * teardown and the next run.
 */
export default async function globalSetup(): Promise<void> {
	for (const action of [...restoreSiteConfiguration(), ...(await restoreSchemaChanges())]) {
		console.warn(`[acceptance setup] ${action} (left behind by an interrupted run).`);
	}
	prepareSiteConfiguration();
}
