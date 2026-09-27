import type { Page } from '@playwright/test';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { getHiddenFields, type HiddenFieldsResponse } from './management-api.js';

/**
 * Content root of the running test site: `dotnet run --project src/Umbraco.Community.PropertyVisibility.TestSite` uses
 * the project folder. Override with PV_SITE_CONTENT_ROOT when the site runs from somewhere else (it must be the folder
 * the site reads appsettings.json and PropertyVisibility.config.json from).
 */
export const SITE_CONTENT_ROOT =
	process.env.PV_SITE_CONTENT_ROOT ??
	fileURLToPath(new URL('../../../src/Umbraco.Community.PropertyVisibility.TestSite/', import.meta.url));

/** The rules file the test site reads (PropertyVisibility:ConfigFile in its appsettings.json). */
export const RULES_FILE = join(SITE_CONTENT_ROOT, 'PropertyVisibility.config.json');

/** The running test site's appsettings.json (reloaded by the host on change). */
export const APPSETTINGS_FILE = join(SITE_CONTENT_ROOT, 'appsettings.json');

/**
 * State that must survive an interrupted run (gitignored). Not the output folder: Playwright empties that at the start
 * of a run.
 */
const RUN_STATE_DIR = fileURLToPath(new URL('../.run-state/', import.meta.url));

/** appsettings.json as it was when the run started (support/global-setup.ts). */
const APPSETTINGS_BACKUP = join(RUN_STATE_DIR, 'appsettings.json.backup');

/** Present while the suite's own rules file exists, so a later run knows it may delete it. */
const RULES_FILE_MARKER = join(RUN_STATE_DIR, 'rules-file-written-by-the-suite');

/**
 * Undoes what an earlier run left behind: restores appsettings.json from the backup taken when that run started, and
 * deletes a rules file the suite wrote. Runs in the global setup (an interrupted or killed run) and teardown (an
 * interrupted test body, whose `finally` Playwright does not run). Returns what it did.
 */
export function restoreSiteConfiguration(): string[] {
	const done: string[] = [];
	if (existsSync(APPSETTINGS_BACKUP)) {
		const backup = readFileSync(APPSETTINGS_BACKUP);
		if (!readFileSync(APPSETTINGS_FILE).equals(backup)) {
			writeFileSync(APPSETTINGS_FILE, backup);
			done.push(`restored ${APPSETTINGS_FILE} from the copy taken when the run started`);
		}
		rmSync(APPSETTINGS_BACKUP, { force: true });
	}
	if (existsSync(RULES_FILE_MARKER)) {
		if (existsSync(RULES_FILE)) {
			rmSync(RULES_FILE, { force: true });
			done.push(`deleted the rules file ${RULES_FILE} the suite had written`);
		}
		rmSync(RULES_FILE_MARKER, { force: true });
	}
	return done;
}

/**
 * Checks that the site is in the state the suite expects (the package enabled in appsettings.json, no rules file) and
 * keeps a copy of appsettings.json for restoreSiteConfiguration(). Throws with the reason otherwise.
 */
export function prepareSiteConfiguration(): void {
	if (existsSync(RULES_FILE)) {
		throw new Error(
			`${RULES_FILE} exists. The acceptance suite expects appsettings to be the active rules source; move the file ` +
				'away (the test site ships PropertyVisibility.config.sample.json instead) and run again.',
		);
	}
	const appSettings = readFileSync(APPSETTINGS_FILE);
	if (propertyVisibilityEnabled(appSettings.toString('utf8')) !== true) {
		throw new Error(`${APPSETTINGS_FILE} must start its PropertyVisibility section with "Enabled": true for the acceptance suite.`);
	}
	mkdirSync(RUN_STATE_DIR, { recursive: true });
	writeFileSync(APPSETTINGS_BACKUP, appSettings);
}

/**
 * A temporary rules file in the running site's content root. `begin()` refuses to start when a rules file already
 * exists (the suite expects appsettings to be the active source and never overwrites someone's file); `restore()`
 * deletes the file again and belongs in a `finally` block. The global teardown deletes a file an interrupted test left.
 */
export class TemporaryRulesFile {
	private constructor() {}

	static begin(): TemporaryRulesFile {
		if (existsSync(RULES_FILE)) {
			throw new Error(
				`${RULES_FILE} already exists. The acceptance suite expects appsettings to be the active rules source; ` +
					'move the file away (the test site ships PropertyVisibility.config.sample.json instead) and run again.',
			);
		}
		return new TemporaryRulesFile();
	}

	/** Writes the file in one go (the package reloads it 250 ms after the last change). */
	write(content: string | object): void {
		mkdirSync(RUN_STATE_DIR, { recursive: true });
		writeFileSync(RULES_FILE_MARKER, '');
		writeFileSync(RULES_FILE, typeof content === 'string' ? content : `${JSON.stringify(content, null, '\t')}\n`);
	}

	/** Deletes the file; the site falls back to the appsettings rules. */
	restore(): void {
		rmSync(RULES_FILE, { force: true });
		rmSync(RULES_FILE_MARKER, { force: true });
	}
}

/**
 * Changes the running site's appsettings.json for one test. `restore()` writes the original bytes back and verifies
 * them byte for byte; it belongs in a `finally` block. The global teardown restores the file when an interrupted test
 * skipped it.
 */
export class TemporaryAppSettings {
	private constructor(readonly original: Buffer) {}

	static begin(): TemporaryAppSettings {
		const current = readFileSync(APPSETTINGS_FILE);
		if (existsSync(APPSETTINGS_BACKUP) && !current.equals(readFileSync(APPSETTINGS_BACKUP))) {
			throw new Error(`${APPSETTINGS_FILE} differs from the copy taken when the run started; an earlier test did not restore it.`);
		}
		return new TemporaryAppSettings(current);
	}

	/** The original file as text. */
	get originalText(): string {
		return this.original.toString('utf8');
	}

	write(text: string): void {
		writeFileSync(APPSETTINGS_FILE, text, 'utf8');
	}

	restore(): void {
		writeFileSync(APPSETTINGS_FILE, this.original);
		if (!readFileSync(APPSETTINGS_FILE).equals(this.original)) {
			throw new Error(`${APPSETTINGS_FILE} could not be restored byte for byte.`);
		}
	}
}

/** `"PropertyVisibility": { "Enabled": true|false` at the start of the section, as the test site's appsettings.json has it. */
const ENABLED_PATTERN = /("PropertyVisibility"\s*:\s*\{\s*"Enabled"\s*:\s*)(true|false)/;

/** The value of `PropertyVisibility:Enabled` when it is the section's first key; undefined otherwise. */
export function propertyVisibilityEnabled(appSettings: string): boolean | undefined {
	const match = ENABLED_PATTERN.exec(appSettings);
	return match ? match[2] === 'true' : undefined;
}

/**
 * The appsettings text with `PropertyVisibility:Enabled` set. Only the value is replaced, so the rest of the file keeps
 * its exact formatting.
 */
export function withPropertyVisibilityEnabled(appSettings: string, enabled: boolean): string {
	if (!ENABLED_PATTERN.test(appSettings)) {
		throw new Error('appsettings.json has no "PropertyVisibility": { "Enabled": ... } as its first key.');
	}
	return appSettings.replace(ENABLED_PATTERN, `$1${enabled}`);
}

/**
 * Polls the hidden-fields API (a direct call, see support/management-api.ts) until the response satisfies the
 * predicate, and returns that response. Configuration reloads are asynchronous: a rules file change is picked up 250 ms
 * after the last write, an appsettings change after the host's reload delay.
 */
export async function pollHiddenFields(
	page: Page,
	query: { documentKey: string; contentTypeKey: string; parentKey?: string },
	predicate: (body: HiddenFieldsResponse) => boolean,
	description: string,
	timeout: number,
): Promise<HiddenFieldsResponse> {
	const deadline = Date.now() + timeout;
	let last: { status: number; body: HiddenFieldsResponse } | undefined;
	for (;;) {
		last = await getHiddenFields(page, query);
		if (last.status === 200 && predicate(last.body)) return last.body;
		if (Date.now() >= deadline) {
			throw new Error(
				`The hidden-fields API did not reach "${description}" within ${timeout} ms. Last response (${last.status}): ` +
					JSON.stringify(last.body),
			);
		}
		await page.waitForTimeout(200);
	}
}

/** True when the response hides exactly these property and container keys (order ignored). */
export function hidesExactly(
	body: HiddenFieldsResponse,
	expected: { propertyKeys: string[]; containerKeys: string[] },
): boolean {
	const same = (a: string[], b: string[]) => a.length === b.length && [...a].sort().every((key, i) => key === [...b].sort()[i]);
	return same(body.propertyTypeKeys, expected.propertyKeys) && same(body.containerKeys, expected.containerKeys);
}
