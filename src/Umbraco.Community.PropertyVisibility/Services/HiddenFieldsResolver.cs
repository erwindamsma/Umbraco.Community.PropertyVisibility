using System.Text;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Default <see cref="IHiddenFieldsResolver" />.
/// </summary>
/// <remarks>
///     <para>
///         Which rules: the blocks keyed by the content type itself, and the blocks keyed by every content type it is
///         composed of, directly or transitively (<see cref="CompositionClosure" />; a parent document type counts, because
///         Umbraco models it as a composition).
///     </para>
///     <para>
///         Where they resolve: the content type's own blocks against its full composed structure; a composition's blocks
///         against that composition's own composed structure, taken from the composition objects Umbraco already holds on
///         the content type. So a container alias in a composition's rule removes the composition's containers of that
///         alias (and those it inherits from its own compositions), not a same-alias container the content type adds
///         itself or gets from another composition. A composed property type or group keeps its key inside the composing
///         type (<see cref="IContentTypeComposition.CompositionPropertyGroups" /> and
///         <see cref="IContentTypeComposition.CompositionPropertyTypes" /> clone them with their keys), so the keys found
///         on the composition are the keys the backoffice shows inside the content type.
///     </para>
///     <para>
///         Properties: every property type of the structure whose alias is in any block's
///         <see cref="ContentTypeVisibilityOptions.Properties" /> (case-insensitive).
///     </para>
///     <para>
///         Containers: for each configured alias, every <see cref="PropertyGroup" /> of the structure whose
///         <see cref="PropertyGroup.Alias" /> equals it (a tab, a root-level group or a <c>tab/group</c> path) or whose
///         parent's alias equals it (the groups under a tab). The group's key goes to the container set and all its property
///         keys to the property set. Same-alias groups contributed by several compositions are distinct objects and are
///         all returned, so the client strips every copy.
///     </para>
///     <para>
///         Parents: a container's parent is the one the Management API reports and the backoffice renders it in
///         (<c>ContentTypeMapDefinition.ParentGroup</c>): the alias up to the first <c>/</c>, looked up (case-sensitively)
///         among the own containers of the content type that holds the container. A container name may contain <c>/</c>:
///         Umbraco builds the alias of group "Image/Video" in tab "Media" as <c>media/image/Video</c>, and its parent is
///         <c>media</c>, not <c>media/image</c>. A container whose first segment names no container of its type (a group in
///         a tab named "Header/Footer") has no parent: the backoffice shows it outside any tab, so a rule on that tab does
///         not reach it either.
///     </para>
///     <para>
///         Emptied containers: after the keys of all rules are united, and on the content type's own final structure,
///         with <c>hideEmptiedContainers</c> a group with at least one property whose properties are now all hidden is
///         added, then a tab with no visible own property whose child groups are all hidden is added (child groups
///         without properties do not count and are hidden with the tab; the tab must have lost at least one property or
///         group to a rule). Copies of a container that the content type and its compositions each hold are evaluated as
///         one merged container when the backoffice renders them as one: same type (tab or group), same name compared
///         the way the backoffice compares it (ignoring case; white space and some punctuation count as a hyphen) and,
///         for a group in a tab, a parent tab of the same name. Two tabs with the same alias but different names, such as
///         "LegacyTab" and "Legacy tab", are two tabs in the backoffice and are evaluated separately.
///     </para>
/// </remarks>
public sealed class HiddenFieldsResolver : IHiddenFieldsResolver
{
	/// <inheritdoc />
	public HiddenFields Resolve(IContentType contentType, RuleBlockLookup ruleBlocksFor, bool hideEmptiedContainers)
	{
		ArgumentNullException.ThrowIfNull(contentType);
		ArgumentNullException.ThrowIfNull(ruleBlocksFor);

		var compositionRules = new List<(IContentTypeComposition Composition, IReadOnlyList<ContentTypeVisibilityOptions> Blocks)>();
		foreach (IContentTypeComposition composition in CompositionClosure.Of(contentType))
		{
			IReadOnlyList<ContentTypeVisibilityOptions> blocks = ruleBlocksFor(composition.Alias);
			if (blocks.Count > 0)
			{
				compositionRules.Add((composition, blocks));
			}
		}

		return ResolveCore(contentType, ruleBlocksFor(contentType.Alias), compositionRules, hideEmptiedContainers);
	}

	/// <inheritdoc />
	public HiddenFields Resolve(IContentType contentType, IReadOnlyList<ContentTypeVisibilityOptions> ruleBlocks, bool hideEmptiedContainers)
	{
		ArgumentNullException.ThrowIfNull(contentType);
		ArgumentNullException.ThrowIfNull(ruleBlocks);

		return ResolveCore(contentType, ruleBlocks, [], hideEmptiedContainers);
	}

	/// <summary>
	///     The container aliases a rule can name on a content type, its compositions included: those the options validator
	///     accepts and that match their container as written. Distinct and sorted, both ignoring case.
	/// </summary>
	/// <param name="contentType">The content type.</param>
	/// <returns>The aliases; empty when the type has no tabs or groups.</returns>
	internal static List<string> ConfigurableContainerAliases(IContentTypeComposition contentType)
		=> contentType.CompositionPropertyGroups
			.Select(group => group.Alias)
			.Where(alias => PropertyVisibilityOptionsValidator.IsValidContainerAlias(alias)
				&& string.Equals(alias, alias.Trim(), StringComparison.Ordinal))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

	private static HiddenFields ResolveCore(
		IContentType contentType,
		IReadOnlyList<ContentTypeVisibilityOptions> ownBlocks,
		List<(IContentTypeComposition Composition, IReadOnlyList<ContentTypeVisibilityOptions> Blocks)> compositionRules,
		bool hideEmptiedContainers)
	{
		var propertyKeys = new HashSet<Guid>();
		var containerKeys = new HashSet<Guid>();
		var anyAlias = false;

		// The content type's own rules, against its full composed structure.
		Unmatched own = HideMatches(contentType, ownBlocks, propertyKeys, containerKeys, ref anyAlias);

		// Each composition's rules, against the composition's own composed structure.
		var unmatchedOnCompositions = new List<UnmatchedCompositionAliases>();
		foreach ((IContentTypeComposition composition, IReadOnlyList<ContentTypeVisibilityOptions> blocks) in compositionRules)
		{
			Unmatched unmatched = HideMatches(composition, blocks, propertyKeys, containerKeys, ref anyAlias);
			if (unmatched.Properties.Count > 0 || unmatched.Containers.Count > 0)
			{
				unmatchedOnCompositions.Add(new UnmatchedCompositionAliases(composition, unmatched.Properties, unmatched.Containers));
			}
		}

		if (!anyAlias)
		{
			return HiddenFields.Empty;
		}

		if (hideEmptiedContainers && propertyKeys.Count > 0)
		{
			List<PropertyGroup> allGroups = contentType.CompositionPropertyGroups.ToList();
			if (allGroups.Count > 0)
			{
				HideEmptiedContainers(contentType, allGroups, propertyKeys, containerKeys);
			}
		}

		return new HiddenFields(propertyKeys, containerKeys, own.Properties, own.Containers)
		{
			UnmatchedOnCompositions = unmatchedOnCompositions,
		};
	}

	// Adds the keys the blocks match in the structure of one content type; returns the aliases that matched nothing there.
	private static Unmatched HideMatches(
		IContentTypeComposition structure,
		IReadOnlyList<ContentTypeVisibilityOptions> blocks,
		HashSet<Guid> propertyKeys,
		HashSet<Guid> containerKeys,
		ref bool anyAlias)
	{
		List<string> propertyAliases = CollectAliases(blocks.Select(block => block.Properties));
		List<string> containerAliases = CollectAliases(blocks.Select(block => block.Containers));
		if (propertyAliases.Count == 0 && containerAliases.Count == 0)
		{
			return Unmatched.None;
		}

		anyAlias = true;
		var unmatchedProperties = new List<string>();
		var unmatchedContainers = new List<string>();

		// Properties.
		if (propertyAliases.Count > 0)
		{
			var matchedPropertyAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IPropertyType property in structure.CompositionPropertyTypes)
			{
				if (propertyAliases.Contains(property.Alias, StringComparer.OrdinalIgnoreCase))
				{
					propertyKeys.Add(property.Key);
					matchedPropertyAliases.Add(property.Alias);
				}
			}

			foreach (var alias in propertyAliases)
			{
				if (!matchedPropertyAliases.Contains(alias))
				{
					unmatchedProperties.Add(alias);
				}
			}
		}

		// Containers.
		if (containerAliases.Count > 0)
		{
			List<PropertyGroup> allGroups = structure.CompositionPropertyGroups.ToList();
			Dictionary<Guid, IReadOnlyCollection<PropertyGroup>> ownerGroups = OwnerGroups(structure);
			List<(PropertyGroup Group, string? ParentAlias)> groups = allGroups
				.Select(group => (group, ParentOf(group, ownerGroups, allGroups)?.Alias))
				.ToList();
			foreach (var alias in containerAliases)
			{
				var matched = false;
				foreach ((PropertyGroup group, var parentAlias) in groups)
				{
					if (string.Equals(group.Alias, alias, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(parentAlias, alias, StringComparison.OrdinalIgnoreCase))
					{
						matched = true;
						HideGroup(group, propertyKeys, containerKeys);
					}
				}

				if (!matched)
				{
					unmatchedContainers.Add(alias);
				}
			}
		}

		return new Unmatched(unmatchedProperties, unmatchedContainers);
	}

	private static List<string> CollectAliases(IEnumerable<List<string>> lists)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var ordered = new List<string>();
		foreach (List<string> list in lists)
		{
			foreach (var raw in list)
			{
				var alias = raw?.Trim();
				if (!string.IsNullOrEmpty(alias) && seen.Add(alias))
				{
					ordered.Add(alias);
				}
			}
		}

		return ordered;
	}

	private static void HideGroup(PropertyGroup group, HashSet<Guid> propertyKeys, HashSet<Guid> containerKeys)
	{
		containerKeys.Add(group.Key);
		if (group.PropertyTypes is null)
		{
			return;
		}

		foreach (IPropertyType property in group.PropertyTypes)
		{
			propertyKeys.Add(property.Key);
		}
	}

	private static void HideEmptiedContainers(
		IContentTypeComposition contentType,
		List<PropertyGroup> allGroups,
		HashSet<Guid> propertyKeys,
		HashSet<Guid> containerKeys)
	{
		List<MergedCopy> copiesWithKeys = MergeKeys(contentType, allGroups);
		List<IGrouping<string, PropertyGroup>> merged = copiesWithKeys
			.GroupBy(copy => copy.Key, copy => copy.Group, StringComparer.Ordinal)
			.ToList();

		// Pass 1: groups (root-level or nested under a tab) whose every property is hidden.
		foreach (IGrouping<string, PropertyGroup> copies in merged)
		{
			if (copies.Any(group => group.Type != PropertyGroupType.Group))
			{
				continue;
			}

			List<IPropertyType> properties = copies.SelectMany(OwnProperties).ToList();
			if (properties.Count > 0 && properties.All(property => propertyKeys.Contains(property.Key)))
			{
				foreach (PropertyGroup group in copies)
				{
					containerKeys.Add(group.Key);
				}
			}
		}

		// Pass 2: tabs with no visible own property whose child groups are all hidden. A child group without any
		// property does not keep a tab visible (the backoffice would render it as an empty box on an otherwise empty
		// tab) and is hidden together with the tab. A tab is only hidden when a rule took something from it: at least one
		// hidden own property or one hidden child group, so an intrinsically empty tab is left alone.
		foreach (IGrouping<string, PropertyGroup> copies in merged)
		{
			if (copies.Any(group => group.Type != PropertyGroupType.Tab))
			{
				continue;
			}

			List<IPropertyType> ownProperties = copies.SelectMany(OwnProperties).ToList();
			List<IGrouping<string, PropertyGroup>> childGroups = copiesWithKeys
				.Where(copy => string.Equals(copy.ParentKey, copies.Key, StringComparison.Ordinal))
				.GroupBy(copy => copy.Key, copy => copy.Group, StringComparer.Ordinal)
				.ToList();
			List<IGrouping<string, PropertyGroup>> childGroupsWithProperties = childGroups
				.Where(childCopies => childCopies.SelectMany(OwnProperties).Any())
				.ToList();

			var ruleTookSomething = ownProperties.Any(property => propertyKeys.Contains(property.Key))
				|| childGroupsWithProperties.Any(childCopies => childCopies.All(group => containerKeys.Contains(group.Key)));
			if (!ruleTookSomething)
			{
				continue;
			}

			if (ownProperties.All(property => propertyKeys.Contains(property.Key))
				&& childGroupsWithProperties.All(childCopies => childCopies.All(group => containerKeys.Contains(group.Key))))
			{
				foreach (PropertyGroup group in copies.Concat(childGroups.SelectMany(childCopies => childCopies)))
				{
					containerKeys.Add(group.Key);
				}
			}
		}
	}

	private static IEnumerable<IPropertyType> OwnProperties(PropertyGroup group)
		=> group.PropertyTypes ?? Enumerable.Empty<IPropertyType>();

	// Group key -> the own containers of the content type that holds the group: the type itself or one of its
	// compositions. An alias only identifies a container within one type, so a parent is looked up there.
	private static Dictionary<Guid, IReadOnlyCollection<PropertyGroup>> OwnerGroups(IContentTypeComposition contentType)
	{
		var ownerGroups = new Dictionary<Guid, IReadOnlyCollection<PropertyGroup>>();
		foreach (IContentTypeComposition owner in CompositionClosure.Of(contentType).Prepend(contentType))
		{
			if (owner.PropertyGroups is not { } ownGroups)
			{
				continue;
			}

			foreach (PropertyGroup group in ownGroups)
			{
				ownerGroups.TryAdd(group.Key, ownGroups);
			}
		}

		return ownerGroups;
	}

	// The container the backoffice renders a container in, as the Management API maps it (ContentTypeMapDefinition.
	// ParentGroup in Umbraco 17): the alias up to the first '/', looked up case-sensitively among the own containers of
	// the type that holds it; null for an alias without '/' or a first segment that names no container of that type. The
	// first segment has no '/', so a parent never has a parent itself. A group the owner map does not know (not expected)
	// is looked up among all containers of the structure.
	private static PropertyGroup? ParentOf(
		PropertyGroup group,
		Dictionary<Guid, IReadOnlyCollection<PropertyGroup>> ownerGroups,
		IReadOnlyCollection<PropertyGroup> allGroups)
	{
		var separator = group.Alias?.IndexOf('/', StringComparison.Ordinal) ?? -1;
		if (separator < 0)
		{
			return null;
		}

		var parentAlias = group.Alias![..separator];
		IEnumerable<PropertyGroup> candidates = ownerGroups.TryGetValue(group.Key, out IReadOnlyCollection<PropertyGroup>? ownGroups)
			? ownGroups
			: allGroups;
		return candidates.FirstOrDefault(candidate => !ReferenceEquals(candidate, group)
			&& string.Equals(candidate.Alias, parentAlias, StringComparison.Ordinal));
	}

	// The key under which the backoffice merges the copies of a container that the content type and its compositions
	// each hold (content-type-structure-manager.class.ts, getContainerChainKey): the container's type and encoded name,
	// after those of its parent (ParentOf, so the parent the backoffice was given).
	private static List<MergedCopy> MergeKeys(IContentTypeComposition contentType, List<PropertyGroup> allGroups)
	{
		Dictionary<Guid, IReadOnlyCollection<PropertyGroup>> ownerGroups = OwnerGroups(contentType);

		var copies = new List<MergedCopy>(allGroups.Count);
		foreach (PropertyGroup group in allGroups)
		{
			var name = ContainerKeySegment(group.Type, group.Name);
			if (ParentOf(group, ownerGroups, allGroups) is not { } parent)
			{
				copies.Add(new MergedCopy(group, name, null));
				continue;
			}

			var parentKey = ContainerKeySegment(parent.Type, parent.Name);
			copies.Add(new MergedCopy(group, $"{parentKey}|{name}", parentKey));
		}

		return copies;
	}

	/// <summary>
	///     The backoffice's <c>encodeFolderName</c> (Umbraco 17), reduced to what decides whether two names are equal.
	/// </summary>
	/// <remarks>
	///     <para>
	///         Lower case; each run of white space and each of the characters <c>_ . ! ~ * ( )</c> becomes <c>-</c>; an
	///         apostrophe is dropped. The backoffice then URI-encodes the rest, which is one-to-one and is left out here,
	///         except for <c>%</c> and <c>|</c>: they are escaped, so no name can produce the <c>|</c> that joins a parent's
	///         segment to its child's in the merge key.
	///     </para>
	///     <para>
	///         Known differences, in characters container names do not use in practice: .NET counts U+0085 (next line) as
	///         white space and JavaScript's <c>\s</c> does not, and the reverse for U+FEFF (zero-width no-break space);
	///         JavaScript's <c>toLowerCase</c> turns a capital sigma at the end of a word into a final small sigma (U+03C2)
	///         and U+0130 (capital I with dot above) into <c>i</c> plus U+0307, while
	///         <see cref="string.ToLowerInvariant" /> does neither. Where they differ, two copies
	///         that the backoffice merges are evaluated here as two containers, or the reverse. That only affects which emptied
	///         containers <c>hideEmptiedContainers</c> adds, not which properties the rules hide.
	///     </para>
	/// </remarks>
	private static string ContainerKeySegment(PropertyGroupType type, string? name)
	{
		var encoded = new StringBuilder(name?.Length ?? 0);
		var inWhiteSpace = false;
		foreach (var character in (name ?? string.Empty).ToLowerInvariant())
		{
			if (char.IsWhiteSpace(character))
			{
				if (!inWhiteSpace)
				{
					encoded.Append('-');
				}

				inWhiteSpace = true;
				continue;
			}

			inWhiteSpace = false;
			switch (character)
			{
				case '\'':
					break;
				case '_' or '.' or '!' or '~' or '*' or '(' or ')':
					encoded.Append('-');
					break;
				case '%':
					encoded.Append("%25");
					break;
				case '|':
					encoded.Append("%7C");
					break;
				default:
					encoded.Append(character);
					break;
			}
		}

		return $"{(type == PropertyGroupType.Tab ? "tab" : "group")}/{encoded}";
	}

	private sealed record MergedCopy(PropertyGroup Group, string Key, string? ParentKey);

	private sealed record Unmatched(IReadOnlyList<string> Properties, IReadOnlyList<string> Containers)
	{
		public static readonly Unmatched None = new([], []);
	}
}
