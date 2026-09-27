import type { HiddenFieldsResponseModel } from '../api/index.js';
import { LOG_PREFIX, RULE_PREFIX } from '../constants.js';
import { debugLog, describeError } from '../debug.js';
import { peekPackageWarning } from '../package-warning.js';
import { dispatchAppliedEvent, type UmbPropertyVisibilityAppliedTarget } from './applied-event.js';
import { rememberRemovedContainers } from './removed-container-rejection.js';
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbContentTypeModel, UmbContentTypeStructureManager } from '@umbraco-cms/backoffice/content-type';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { UmbObserverController } from '@umbraco-cms/backoffice/observable-api';
import type { UmbVariantPropertyGuardManager } from '@umbraco-cms/backoffice/property';

/**
 * What the applier needs from a workspace: the document workspace context and each block element manager
 * (content and settings) expose both members.
 */
export interface VisibilityTarget {
	propertyViewGuard: UmbVariantPropertyGuardManager;
	structure: UmbContentTypeStructureManager;
}

/** Which document and content type a response belongs to; reported in the applied event. */
export interface UmbHiddenFieldsApplySubject {
	/** The document the workspace edits (for a block half: the document that holds the block). */
	documentKey: string;
	/** The document type or element type the response was requested for. */
	contentTypeKey: string;
}

/**
 * One warning and one notification per browser session when container hiding degrades to properties only. Set only
 * for a missing or broken `removeContainer`, so an unrelated failure never hides a later genuine report.
 */
let containerHidingUnavailableReported = false;

/**
 * The only code that touches property view guards and the content type structure.
 *
 * Properties are hidden with view guard rules (`permitted: false` for every culture); tabs and groups are removed
 * from the in-memory structure with `removeContainer(..., { preventRemovingProperties: true })`, so the property
 * definitions and their values survive every reload of the workspace. The structure is re-applied on every
 * structure emission (compositions load later, blocks reload), containers already gone are never touched again,
 * and an emission that arrives during a run is replayed once afterwards.
 *
 * A property of a removed tab or group keeps pointing at it. When such a property fails validation (a hidden mandatory
 * property, PV203), Umbraco's validation-to-hints manager throws in an unhandled promise because it finds no container;
 * removed-container-rejection.ts marks exactly that rejection as handled.
 *
 * After every completed pass (and after an apply that has nothing to hide) the applier dispatches the
 * `umbraco-community-property-visibility:applied` window event (applied-event.ts).
 */
export class UmbHiddenFieldsApplierController extends UmbControllerBase {
	#kind: UmbPropertyVisibilityAppliedTarget;
	#target?: VisibilityTarget;
	#hidden?: HiddenFieldsResponseModel;
	#subject?: UmbHiddenFieldsApplySubject;
	#contentTypes: Array<UmbContentTypeModel> = [];
	#appliedRuleUniques: Array<string> = [];
	#structureObserver?: UmbObserverController<Array<UmbContentTypeModel>>;
	#inFlight = false;
	#pending = false;
	#localize = new UmbLocalizationController(this);

	/**
	 * @param host The controller host (the workspace context that owns this applier).
	 * @param kind What this applier works on, reported as `target` in the applied event.
	 */
	constructor(host: UmbControllerHost, kind: UmbPropertyVisibilityAppliedTarget = 'document') {
		super(host);
		this.#kind = kind;
	}

	/**
	 * Applies a response to a target. Resets first, so a call never inherits the observer or the response of an
	 * earlier document; adds the property guard rules right away, waits for the structure to load, then removes the
	 * containers and re-applies on every structure emission.
	 * A disabled or empty response dispatches the applied event right after the reset (nothing to wait for).
	 * Rejects only when the structure fails to load; the caller fails open, and the applied event reports nothing hidden.
	 */
	async apply(
		target: VisibilityTarget,
		hidden: HiddenFieldsResponseModel,
		subject: UmbHiddenFieldsApplySubject,
	): Promise<void> {
		this.reset();
		this.#target = target;
		this.#hidden = hidden;
		this.#subject = subject;

		if (hidden.disabled || (hidden.propertyTypeKeys.length === 0 && hidden.containerKeys.length === 0)) {
			this.#dispatchApplied(subject, 0, 0);
			return;
		}

		// Synchronously, in the same task as reset(): when the same document is re-applied (the first save flips isNew,
		// a parent re-anchor) the rules reset() just removed come back before Lit renders, so a guarded property editor
		// is never created and torn down in between. Guard rules do not depend on the structure.
		this.#applyPropertyRules(target, hidden);

		try {
			await target.structure.whenLoaded();
		} catch (error) {
			// Fail open for this response only: drop its rules, report nothing hidden, let the caller log it.
			if (this.#isCurrent(target, hidden)) {
				this.reset();
				this.#dispatchApplied(subject, 0, 0);
			}
			throw error;
		}

		// A reset (document switch) or a newer apply may have happened while waiting.
		if (!this.#isCurrent(target, hidden)) return;

		this.#structureObserver = this.observe(
			target.structure.contentTypes,
			(contentTypes) => {
				this.#contentTypes = contentTypes ?? [];
				// A guard manager or structure call that throws ends this pass without its applied event; the next emission
				// tries again. Caught here, so it never becomes an unhandled rejection.
				this.#reapply().catch((error: unknown) => {
					console.warn(
						`${LOG_PREFIX} could not apply the hidden fields to the workspace structure.`,
						describeError(error),
					);
				});
			},
			'observeStructure',
		);
	}

	/**
	 * Synchronous: removes the guard rules this applier added, stops observing the structure and discards the
	 * response. Called by the workspace context on the emission that fires inside the workspace's resetState(),
	 * before the structure is cleared, so a stale response never strips the next document's structure.
	 * Removed containers stay removed until the workspace itself clears the structure.
	 * Dispatches nothing: the next apply() reports, including one that has nothing to hide.
	 */
	reset(): void {
		const target = this.#target;
		const uniques = this.#appliedRuleUniques;

		this.#hidden = undefined;
		this.#target = undefined;
		this.#subject = undefined;
		this.#contentTypes = [];
		this.#appliedRuleUniques = [];
		this.#pending = false;

		this.#structureObserver?.destroy();
		this.#structureObserver = undefined;

		if (target && uniques.length > 0) {
			try {
				target.propertyViewGuard.removeRules(uniques);
			} catch (error) {
				console.warn(`${LOG_PREFIX} could not remove property view guard rules`, describeError(error));
			}
		}
	}

	override destroy(): void {
		this.reset();
		super.destroy();
	}

	async #reapply(): Promise<void> {
		if (this.#inFlight) {
			// An emission during the awaited loop: run once more when it finishes.
			this.#pending = true;
			return;
		}

		this.#inFlight = true;
		let completed: { target: VisibilityTarget; hidden: HiddenFieldsResponseModel } | undefined;
		try {
			do {
				this.#pending = false;
				completed = await this.#applyOnce();
			} while (this.#pending);
		} finally {
			this.#inFlight = false;
		}

		// Only when the last pass ran to the end for the response that is still current: a pass cut short by a reset or
		// a newer apply reports nothing, the newer apply's own pass reports instead. Checked again here because a reset
		// or a newer apply can run in the microtask between the end of the pass and this line.
		const subject = this.#subject;
		if (completed && subject && this.#isCurrent(completed.target, completed.hidden)) {
			this.#dispatchApplied(
				subject,
				this.#appliedRuleUniques.length,
				this.#countAbsentContainers(completed.hidden),
			);
		}
	}

	/** One pass for the current response; the response it completed for, or undefined when it was cut short. */
	async #applyOnce(): Promise<{ target: VisibilityTarget; hidden: HiddenFieldsResponseModel } | undefined> {
		const target = this.#target;
		const hidden = this.#hidden;
		if (!target || !hidden) return undefined;

		this.#applyPropertyRules(target, hidden);
		await this.#removeContainers(target, hidden);
		return this.#isCurrent(target, hidden) ? { target, hidden } : undefined;
	}

	#isCurrent(target: VisibilityTarget, hidden: HiddenFieldsResponseModel): boolean {
		return this.#target === target && this.#hidden === hidden;
	}

	/** The response's container keys no longer present in the latest structure emission. */
	#countAbsentContainers(hidden: HiddenFieldsResponseModel): number {
		if (hidden.containerKeys.length === 0) return 0;
		const present = new Set<string>();
		for (const contentType of this.#contentTypes) {
			for (const container of contentType.containers ?? []) present.add(container.id);
		}
		return hidden.containerKeys.filter((key) => !present.has(key)).length;
	}

	#dispatchApplied(subject: UmbHiddenFieldsApplySubject, propertyCount: number, containerCount: number): void {
		dispatchAppliedEvent({
			documentKey: subject.documentKey,
			contentTypeKey: subject.contentTypeKey,
			propertyCount,
			containerCount,
			target: this.#kind,
		});
	}

	#applyPropertyRules(target: VisibilityTarget, hidden: HiddenFieldsResponseModel): void {
		const uniques = hidden.propertyTypeKeys.map((key) => RULE_PREFIX + key);
		const unchanged =
			uniques.length === this.#appliedRuleUniques.length &&
			uniques.every((unique, index) => unique === this.#appliedRuleUniques[index]);
		if (unchanged) return;

		if (this.#appliedRuleUniques.length > 0) {
			target.propertyViewGuard.removeRules(this.#appliedRuleUniques);
		}
		this.#appliedRuleUniques = uniques;

		if (hidden.propertyTypeKeys.length > 0) {
			// No variantId: the rule applies to every culture and segment.
			target.propertyViewGuard.addRules(
				hidden.propertyTypeKeys.map((key) => ({
					unique: RULE_PREFIX + key,
					permitted: false,
					propertyType: { unique: key },
				})),
			);
		}
	}

	async #removeContainers(target: VisibilityTarget, hidden: HiddenFieldsResponseModel): Promise<void> {
		if (hidden.containerKeys.length === 0) return;

		// The only probe: Function.length of removeContainer is 1, so a parameter count check would disable the feature.
		if (typeof target.structure.removeContainer !== 'function') {
			this.#reportContainerHidingUnavailable('structure.removeContainer is not a function');
			return;
		}

		const containerKeys = new Set(hidden.containerKeys);

		for (const contentType of this.#contentTypes) {
			for (const container of contentType.containers ?? []) {
				if (!containerKeys.has(container.id)) continue;
				// Bail out when a reset or a newer apply happened during an awaited call.
				if (this.#target !== target || this.#hidden !== hidden) return;
				// Only containers still present are touched (a removed tab takes its groups with it), so re-runs terminate.
				if (!this.#isContainerPresent(contentType.unique, container.id)) continue;

				// The container and the groups it takes with it: a validation message of one of their properties has no tab
				// or group to show its hint on, and Umbraco's rejection for that is marked handled.
				rememberRemovedContainers([
					container.id,
					...(contentType.containers ?? []).filter((x) => x.parent?.id === container.id).map((x) => x.id),
				]);

				try {
					await target.structure.removeContainer(contentType.unique, container.id, {
						preventRemovingProperties: true,
					});
				} catch (error) {
					// A call that fails after a reset or a newer apply (the structure was cleared or destroyed meanwhile, for
					// example when a block closes) says nothing about the Umbraco version: stop quietly.
					if (!this.#isCurrent(target, hidden)) return;
					// A TypeError is what a changed removeContainer signature or a missing internal member throws: the
					// feature does not work in this Umbraco version. Anything else (for example "Could not find the Content
					// Type to remove container from" while a composition reloads) concerns this container only: skip it,
					// the next structure emission tries again.
					if (error instanceof TypeError) {
						this.#reportContainerHidingUnavailable(error);
						return;
					}
					debugLog('could not remove a tab or group; the next structure change tries again', {
						contentTypeKey: contentType.unique,
						containerKey: container.id,
						error: describeError(error),
					});
				}
			}
		}
	}

	#isContainerPresent(contentTypeUnique: string, containerId: string): boolean {
		const contentType = this.#contentTypes.find((x) => x.unique === contentTypeUnique);
		return contentType?.containers?.some((x) => x.id === containerId) ?? false;
	}

	/** Once per browser session: a console warning and a warning notification headed with the package name. */
	#reportContainerHidingUnavailable(reason: string | TypeError): void {
		if (containerHidingUnavailableReported) return;
		containerHidingUnavailableReported = true;

		console.warn(
			`${LOG_PREFIX} hiding tabs and groups is unavailable in this Umbraco version; properties are still hidden.`,
			typeof reason === 'string' ? reason : describeError(reason),
		);

		// Never rejects: a lookup that fails (the workspace closed meanwhile) only writes a debug line.
		void peekPackageWarning(this, this.#localize, 'propertyVisibility_containerHidingUnavailable');
	}
}
