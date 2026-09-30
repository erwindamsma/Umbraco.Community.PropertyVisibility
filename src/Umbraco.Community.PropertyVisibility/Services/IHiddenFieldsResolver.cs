using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     The property types and containers to hide on one content type, plus the configured aliases that matched nothing.
/// </summary>
/// <param name="PropertyTypeKeys">Keys of every property type to hide, including the properties of hidden containers.</param>
/// <param name="ContainerKeys">Keys of every property group (tab or group) to hide, one per composition copy.</param>
/// <param name="UnmatchedPropertyAliases">
///     Property aliases of the rules keyed by the content type itself that do not exist on it (own properties and
///     compositions checked).
/// </param>
/// <param name="UnmatchedContainerAliases">
///     Container aliases of the rules keyed by the content type itself that do not exist on it (own containers and
///     compositions checked).
/// </param>
public sealed record HiddenFields(
	IReadOnlySet<Guid> PropertyTypeKeys,
	IReadOnlySet<Guid> ContainerKeys,
	IReadOnlyList<string> UnmatchedPropertyAliases,
	IReadOnlyList<string> UnmatchedContainerAliases)
{
	/// <summary>
	///     Shared instance for "nothing to hide".
	/// </summary>
	public static readonly HiddenFields Empty = new(new HashSet<Guid>(), new HashSet<Guid>(), [], []);

	/// <summary>
	///     Gets the aliases of rules keyed by a composition of the content type that do not exist on that composition: one
	///     entry per composition with such aliases, depth first in the order of
	///     <see cref="IContentTypeComposition.ContentTypeComposition" />. An alias is reported against the composition its
	///     rule is keyed by, never against the content type being edited. Always empty when only the content type's own
	///     rules were resolved.
	/// </summary>
	public IReadOnlyList<UnmatchedCompositionAliases> UnmatchedOnCompositions { get; init; } = [];
}

/// <summary>
///     Aliases in the rules keyed by one composition that do not exist on that composition.
/// </summary>
/// <param name="Composition">The composition the rules are keyed by; its own structure is what the aliases were checked against.</param>
/// <param name="PropertyAliases">Property aliases that do not exist on the composition (its own compositions included).</param>
/// <param name="ContainerAliases">Container aliases that do not exist on the composition (its own compositions included).</param>
public sealed record UnmatchedCompositionAliases(
	IContentTypeComposition Composition,
	IReadOnlyList<string> PropertyAliases,
	IReadOnlyList<string> ContainerAliases);

/// <summary>
///     Returns the rule blocks keyed by one content type alias (the global block, the blocks of the rule sets the matched
///     site includes, and the site's own block).
/// </summary>
/// <param name="contentTypeAlias">A content type alias, matched case-insensitively.</param>
/// <returns>The blocks, in any order; empty when no rule is keyed by the alias.</returns>
public delegate IReadOnlyList<ContentTypeVisibilityOptions> RuleBlockLookup(string contentTypeAlias);

/// <summary>
///     Pure resolver from rule blocks to property and container keys; no IO.
/// </summary>
public interface IHiddenFieldsResolver
{
	/// <summary>
	///     Resolves everything that applies to a content type: the rules keyed by the type itself, against its full
	///     composed structure, and the rules keyed by each type it is composed of (directly or transitively, parent types
	///     included), each against that composition's own composed structure. The keys are united, then
	///     <paramref name="hideEmptiedContainers" /> is applied to the content type's final structure.
	/// </summary>
	/// <param name="contentType">The document or element type being edited.</param>
	/// <param name="ruleBlocksFor">The rule blocks keyed by a content type alias; called for the type and each composition.</param>
	/// <param name="hideEmptiedContainers">Whether to also hide containers whose every property ends up hidden.</param>
	/// <returns>The keys to hide; never <c>null</c>.</returns>
	HiddenFields Resolve(IContentType contentType, RuleBlockLookup ruleBlocksFor, bool hideEmptiedContainers);

	/// <summary>
	///     Resolves the union of <paramref name="ruleBlocks" /> against the content type's composed structure, as rules keyed
	///     by that content type. Rules keyed by its compositions are not looked up; the configuration analyzer checks each
	///     configured key against its own type this way.
	/// </summary>
	/// <param name="contentType">The document or element type the blocks are keyed by.</param>
	/// <param name="ruleBlocks">The rule blocks (global, rule set and site), in any order.</param>
	/// <param name="hideEmptiedContainers">Whether to also hide containers whose every property ends up hidden.</param>
	/// <returns>The keys to hide; never <c>null</c>.</returns>
	HiddenFields Resolve(IContentType contentType, IReadOnlyList<ContentTypeVisibilityOptions> ruleBlocks, bool hideEmptiedContainers);
}
