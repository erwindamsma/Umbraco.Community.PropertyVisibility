using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Tests.Common.Builders;
using Umbraco.Cms.Tests.Common.Builders.Extensions;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     Small fluent layer over Umbraco's <see cref="ContentTypeBuilder" /> for content types with tabs, groups and
///     compositions.
/// </summary>
/// <remarks>
///     Every group and property type gets a unique, non-zero id, as it would after persisting. This matters:
///     <see cref="PropertyGroup.Equals(PropertyGroup)" /> treats two groups with the same type, alias and id as equal,
///     so same-alias groups from two compositions only survive the <c>Union</c> in
///     <see cref="IContentTypeComposition.CompositionPropertyGroups" /> when their ids differ, which they always do in a
///     real database.
/// </remarks>
internal sealed class TestContentType
{
	private static int s_lastId = 1000;

	private readonly ContentTypeBuilder _builder;
	private readonly List<IContentTypeComposition> _compositions = [];

	private TestContentType(string alias, bool isElement)
		=> _builder = new ContentTypeBuilder()
			.WithId(NextId())
			.WithKey(Guid.NewGuid())
			.WithAlias(alias)
			.WithName(alias)
			.WithIsElement(isElement);

	/// <summary>Starts a document type.</summary>
	public static TestContentType Document(string alias) => new(alias, isElement: false);

	/// <summary>Starts an element type (used by blocks).</summary>
	public static TestContentType Element(string alias) => new(alias, isElement: true);

	/// <summary>Adds a tab, optionally with properties placed directly on the tab.</summary>
	public TestContentType Tab(string alias, params string[] propertyAliases)
		=> Container(alias, alias, PropertyGroupType.Tab, propertyAliases);

	/// <summary>Adds a tab whose name differs from its alias.</summary>
	public TestContentType NamedTab(string alias, string name, params string[] propertyAliases)
		=> Container(alias, name, PropertyGroupType.Tab, propertyAliases);

	/// <summary>Adds a group: <c>tab/group</c> for a group inside a tab, a plain alias for a root-level group.</summary>
	public TestContentType Group(string alias, params string[] propertyAliases)
		=> Container(alias, alias, PropertyGroupType.Group, propertyAliases);

	/// <summary>Adds a group whose name differs from its alias.</summary>
	public TestContentType NamedGroup(string alias, string name, params string[] propertyAliases)
		=> Container(alias, name, PropertyGroupType.Group, propertyAliases);

	/// <summary>Adds a composition, applied after the type is built (as the content type editor does).</summary>
	public TestContentType ComposedOf(IContentTypeComposition composition)
	{
		_compositions.Add(composition);
		return this;
	}

	/// <summary>
	///     Creates the type under a parent document type. Umbraco adds the parent to the type's compositions (the
	///     Management API calls it inheritance), as the constructor with a parent does here.
	/// </summary>
	public TestContentType Parent(IContentType parent)
	{
		_builder.WithParentContentType(parent);
		return this;
	}

	/// <summary>Builds the content type and adds the compositions.</summary>
	public IContentType Build()
	{
		IContentType contentType = _builder.Build();
		foreach (IContentTypeComposition composition in _compositions)
		{
			if (!contentType.AddContentType(composition))
			{
				throw new InvalidOperationException($"Could not add composition '{composition.Alias}' to '{contentType.Alias}'.");
			}
		}

		return contentType;
	}

	/// <summary>Key of the (own or composed) property type with the given alias.</summary>
	public static Guid PropertyKey(IContentTypeComposition contentType, string alias)
		=> contentType.CompositionPropertyTypes.Single(property => property.Alias == alias).Key;

	/// <summary>Keys of the (own or composed) property types with the given aliases.</summary>
	public static Guid[] PropertyKeys(IContentTypeComposition contentType, params string[] aliases)
		=> aliases.Select(alias => PropertyKey(contentType, alias)).ToArray();

	/// <summary>Key of the single (own or composed) container with the given alias.</summary>
	public static Guid ContainerKey(IContentTypeComposition contentType, string alias)
		=> contentType.CompositionPropertyGroups.Single(group => group.Alias == alias).Key;

	/// <summary>Keys of every (own or composed) container with the given aliases, one per composition copy.</summary>
	public static Guid[] ContainerKeys(IContentTypeComposition contentType, params string[] aliases)
		=> contentType.CompositionPropertyGroups
			.Where(group => aliases.Contains(group.Alias))
			.Select(group => group.Key)
			.ToArray();

	private static int NextId() => Interlocked.Increment(ref s_lastId);

	private TestContentType Container(string alias, string name, PropertyGroupType type, string[] propertyAliases)
	{
		PropertyGroupBuilder<ContentTypeBuilder> group = _builder
			.AddPropertyGroup()
			.WithId(NextId())
			.WithKey(Guid.NewGuid())
			.WithAlias(alias)
			.WithName(name)
			.WithType(type);

		foreach (var propertyAlias in propertyAliases)
		{
			group.AddPropertyType()
				.WithId(NextId())
				.WithKey(Guid.NewGuid())
				.WithAlias(propertyAlias)
				.WithName(propertyAlias)
				.Done();
		}

		return this;
	}
}
