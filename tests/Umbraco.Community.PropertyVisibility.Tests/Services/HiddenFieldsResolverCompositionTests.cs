using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     Rules keyed by a composition (<see cref="IHiddenFieldsResolver.Resolve(IContentType, RuleBlockLookup, bool)" />): a
///     rule keyed by type X applies to X and to every type composed of X, directly or transitively (parent types count),
///     and resolves against X's own composed structure only.
/// </summary>
[TestFixture]
public sealed class HiddenFieldsResolverCompositionTests
{
	private HiddenFieldsResolver _resolver = null!;

	// siteSettings
	// ├─ seoTab (tab)
	// │  └─ seoTab/meta: metaKeywords, metaTitle
	// └─ navigation (root-level group): hideFromSitemap, navigationTitle
	private IContentType _siteSettings = null!;

	// article, composed of siteSettings
	// ├─ contentTab (tab)
	// │  └─ contentTab/main: title, bodyText
	// └─ seoTab (tab, its own copy of the alias)
	//    └─ seoTab/social: socialImage
	private IContentType _article = null!;

	[SetUp]
	public void SetUp()
	{
		_resolver = new HiddenFieldsResolver();
		_siteSettings = Document("siteSettings")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaKeywords", "metaTitle")
			.Group("navigation", "hideFromSitemap", "navigationTitle")
			.Build();
		_article = Document("article")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bodyText")
			.Tab("seoTab")
			.Group("seoTab/social", "socialImage")
			.ComposedOf(_siteSettings)
			.Build();
	}

	[Test]
	public void Composed_property_types_and_groups_keep_their_keys_inside_the_composing_type()
	{
		// The keys found on the composition are the keys the backoffice shows inside the composing type.
		Assert.Multiple(() =>
		{
			Assert.That(PropertyKey(_article, "hideFromSitemap"), Is.EqualTo(PropertyKey(_siteSettings, "hideFromSitemap")));
			Assert.That(ContainerKey(_article, "navigation"), Is.EqualTo(ContainerKey(_siteSettings, "navigation")));
			Assert.That(ContainerKey(_article, "seoTab/meta"), Is.EqualTo(ContainerKey(_siteSettings, "seoTab/meta")));
			Assert.That(ContainerKeys(_article, "seoTab"), Does.Contain(ContainerKey(_siteSettings, "seoTab")));
		});
	}

	[Test]
	public void A_rule_on_a_composition_hides_its_property_in_the_composing_type()
	{
		HiddenFields result = _resolver.Resolve(_article, Rules(("siteSettings", Block(properties: ["hideFromSitemap"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_siteSettings, "hideFromSitemap") }));
			Assert.That(result.ContainerKeys, Is.Empty, "navigation still shows navigationTitle");
			Assert.That(result.UnmatchedPropertyAliases, Is.Empty);
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
			Assert.That(result.UnmatchedOnCompositions, Is.Empty);
		});
	}

	[Test]
	public void A_rule_on_a_composition_also_applies_to_the_composition_itself()
	{
		HiddenFields result = _resolver.Resolve(_siteSettings, Rules(("siteSettings", Block(containers: ["seoTab"]))), hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_siteSettings, "seoTab", "seoTab/meta")));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_siteSettings, "metaKeywords", "metaTitle")));
		});
	}

	[Test]
	public void A_tab_rule_on_a_composition_removes_only_the_compositions_tab_and_groups()
	{
		Guid[] compositionContainers = ContainerKeys(_siteSettings, "seoTab", "seoTab/meta");
		Guid ownSeoTab = _article.PropertyGroups.Single(group => group.Alias == "seoTab").Key;

		HiddenFields viaComposition = _resolver.Resolve(_article, Rules(("siteSettings", Block(containers: ["seoTab"]))), hideEmptiedContainers: true);
		HiddenFields viaArticle = _resolver.Resolve(_article, Rules(("article", Block(containers: ["seoTab"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(viaComposition.ContainerKeys, Is.EquivalentTo(compositionContainers));
			Assert.That(viaComposition.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_article, "metaKeywords", "metaTitle")));
			Assert.That(viaComposition.ContainerKeys, Does.Not.Contain(ownSeoTab), "the article's own seoTab stays");
			Assert.That(viaComposition.ContainerKeys, Does.Not.Contain(ContainerKey(_article, "seoTab/social")));
			Assert.That(viaComposition.PropertyTypeKeys, Does.Not.Contain(PropertyKey(_article, "socialImage")));
			Assert.That(viaComposition.UnmatchedOnCompositions, Is.Empty);

			// The same alias keyed by the article itself resolves against the article's full structure: every copy.
			Assert.That(viaArticle.ContainerKeys, Is.EquivalentTo(compositionContainers.Append(ownSeoTab).Append(ContainerKey(_article, "seoTab/social"))));
		});
	}

	[Test]
	public void A_rule_on_a_nested_composition_reaches_the_outermost_type()
	{
		// page composes pageBase, which composes seo.
		IContentType seo = Document("seo").Group("meta", "metaTitle").Build();
		IContentType pageBase = Document("pageBase").Group("base", "pageTitle").ComposedOf(seo).Build();
		IContentType page = Document("page").Group("body", "text").ComposedOf(pageBase).Build();

		HiddenFields result = _resolver.Resolve(
			page,
			Rules(("seo", Block(properties: ["metaTitle"])), ("pageBase", Block(containers: ["meta"]))),
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(seo, "metaTitle") }));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(seo, "meta") }), "pageBase's structure includes the meta group it composes");
			Assert.That(result.UnmatchedOnCompositions, Is.Empty);
		});
	}

	[Test]
	public void A_parent_document_type_counts_as_a_composition()
	{
		IContentType basePage = Document("basePage").Tab("settingsTab").Group("settingsTab/general", "hideFromNavigation").Build();
		IContentType newsPage = Document("newsPage").Parent(basePage).Group("content", "headline").Build();
		Assert.That(newsPage.ParentId, Is.EqualTo(basePage.Id), "precondition: created under basePage");

		HiddenFields result = _resolver.Resolve(newsPage, Rules(("basePage", Block(containers: ["settingsTab"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(basePage, "settingsTab", "settingsTab/general")));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(basePage, "hideFromNavigation") }));
		});
	}

	[Test]
	public void The_types_own_rules_and_its_compositions_rules_global_and_per_site_are_united()
	{
		HiddenFields result = _resolver.Resolve(
			_article,
			Rules(
				("article", Block(properties: ["bodyText"])),
				("article", Block(containers: ["seoTab/social"])),
				("siteSettings", Block(properties: ["hideFromSitemap"])),
				("siteSettings", Block(containers: ["seoTab/meta"]))),
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(
				result.PropertyTypeKeys,
				Is.EquivalentTo(PropertyKeys(_article, "bodyText", "socialImage", "hideFromSitemap", "metaKeywords", "metaTitle")));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_article, "seoTab/social", "seoTab/meta")));
		});
	}

	[Test]
	public void Keys_reached_by_several_rules_are_returned_once()
	{
		HiddenFields result = _resolver.Resolve(
			_article,
			Rules(
				("article", Block(properties: ["metaKeywords", "hideFromSitemap"], containers: ["seoTab"])),
				("siteSettings", Block(properties: ["METAKEYWORDS", "hideFromSitemap"], containers: ["seoTab", "seoTab/meta"])),
				("siteSettings", Block(properties: ["metaKeywords"], containers: ["SEOTAB"]))),
			hideEmptiedContainers: true);

		Guid[] expectedProperties = PropertyKeys(_article, "metaKeywords", "metaTitle", "hideFromSitemap", "socialImage");
		Guid[] expectedContainers = ContainerKeys(_article, "seoTab", "seoTab/meta", "seoTab/social");

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Has.Count.EqualTo(expectedProperties.Length));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(expectedProperties));
			Assert.That(result.ContainerKeys, Has.Count.EqualTo(expectedContainers.Length));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(expectedContainers));
		});
	}

	[Test]
	public void An_element_type_with_a_composition_resolves_the_compositions_rules()
	{
		IContentType anchorSettings = Element("anchorSettings").Group("anchor", "anchorId").Build();
		IContentType promoBannerSettings = Element("promoBannerSettings")
			.Group("layout", "cssClass")
			.ComposedOf(anchorSettings)
			.Build();

		HiddenFields result = _resolver.Resolve(promoBannerSettings, Rules(("anchorSettings", Block(properties: ["anchorId"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(promoBannerSettings.IsElement, Is.True);
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(anchorSettings, "anchorId") }));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(anchorSettings, "anchor") }), "the emptied anchor group");
		});
	}

	[Test]
	public void An_unknown_alias_in_a_composition_rule_is_reported_against_the_composition()
	{
		// title, contentTab and seoTab/social exist on the article, not on siteSettings: nothing is hidden for them.
		HiddenFields result = _resolver.Resolve(
			_article,
			Rules(
				("siteSettings", Block(properties: ["title", "noSuchProperty", "hideFromSitemap"], containers: ["contentTab", "seoTab/social"])),
				("article", Block(properties: ["noSuchArticleProperty"]))),
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_article, "hideFromSitemap") }));
			Assert.That(result.ContainerKeys, Is.Empty);
			Assert.That(result.UnmatchedPropertyAliases, Is.EqualTo(new[] { "noSuchArticleProperty" }));
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
			Assert.That(result.UnmatchedOnCompositions, Has.Count.EqualTo(1));
			Assert.That(result.UnmatchedOnCompositions[0].Composition, Is.SameAs(_article.ContentTypeComposition.Single()));
			Assert.That(result.UnmatchedOnCompositions[0].Composition.Alias, Is.EqualTo("siteSettings"));
			Assert.That(result.UnmatchedOnCompositions[0].PropertyAliases, Is.EqualTo(new[] { "title", "noSuchProperty" }));
			Assert.That(result.UnmatchedOnCompositions[0].ContainerAliases, Is.EqualTo(new[] { "contentTab", "seoTab/social" }));
		});
	}

	[Test]
	public void Hide_emptied_containers_runs_on_the_final_structure_after_composition_rules()
	{
		// A tab emptied only by a composition rule disappears.
		IContentType seoSettings = Document("seoSettings").Tab("seoTab").Group("seoTab/meta", "metaKeywords", "metaTitle").Build();
		IContentType page = Document("page").Tab("contentTab").Group("contentTab/main", "title").ComposedOf(seoSettings).Build();

		HiddenFields emptied = _resolver.Resolve(page, Rules(("seoSettings", Block(properties: ["metaKeywords", "metaTitle"]))), hideEmptiedContainers: true);
		HiddenFields off = _resolver.Resolve(page, Rules(("seoSettings", Block(properties: ["metaKeywords", "metaTitle"]))), hideEmptiedContainers: false);

		// In the article, seoTab also has the article's own seoTab/social group: the merged tab keeps it until the
		// article's own rule hides socialImage too, and then every copy of the tab goes.
		HiddenFields partly = _resolver.Resolve(_article, Rules(("siteSettings", Block(properties: ["metaKeywords", "metaTitle"]))), hideEmptiedContainers: true);
		HiddenFields united = _resolver.Resolve(
			_article,
			Rules(("siteSettings", Block(properties: ["metaKeywords", "metaTitle"])), ("article", Block(properties: ["socialImage"]))),
			hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(emptied.ContainerKeys, Is.EquivalentTo(ContainerKeys(seoSettings, "seoTab", "seoTab/meta")));
			Assert.That(off.ContainerKeys, Is.Empty);
			Assert.That(partly.ContainerKeys, Is.EquivalentTo(ContainerKeys(_article, "seoTab/meta")));
			Assert.That(united.ContainerKeys, Is.EquivalentTo(ContainerKeys(_article, "seoTab", "seoTab/meta", "seoTab/social")));
		});
	}

	[Test]
	public void Hide_emptied_containers_merges_copies_by_name_as_the_backoffice_does_not_by_alias()
	{
		// Both names give the alias legacyTab, but the backoffice merges tabs by name (encodeFolderName: lower case, white
		// space as '-'), so "LegacyTab" (legacytab) and "Legacy tab" (legacy-tab) are two tabs there.
		IContentType legacySettings = Document("legacySettings").NamedTab("legacyTab", "LegacyTab", "siteTitle").Build();
		IContentType site = Document("site").NamedTab("legacyTab", "Legacy tab", "siteNotes").ComposedOf(legacySettings).Build();
		Assert.That(ContainerKeys(site, "legacyTab"), Has.Length.EqualTo(2), "precondition: two copies of the alias");

		HiddenFields result = _resolver.Resolve(site, Rules(("legacySettings", Block(properties: ["siteTitle"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(legacySettings, "siteTitle") }));
			Assert.That(
				result.ContainerKeys,
				Is.EquivalentTo(new[] { ContainerKey(legacySettings, "legacyTab") }),
				"the composition's LegacyTab is a tab of its own in the backoffice and is now empty; the site's Legacy tab still shows siteNotes");
		});
	}

	[Test]
	public void Hide_emptied_containers_merges_same_name_copies_ignoring_case()
	{
		// "Legacy tab" and "Legacy Tab" are one tab in the backoffice, which still shows siteNotes.
		IContentType legacySettings = Document("legacySettings").NamedTab("legacyTab", "Legacy tab", "siteTitle").Build();
		IContentType site = Document("site").NamedTab("legacyTab", "Legacy Tab", "siteNotes").ComposedOf(legacySettings).Build();

		HiddenFields partly = _resolver.Resolve(site, Rules(("legacySettings", Block(properties: ["siteTitle"]))), hideEmptiedContainers: true);
		HiddenFields fully = _resolver.Resolve(
			site,
			Rules(("legacySettings", Block(properties: ["siteTitle"])), ("site", Block(properties: ["siteNotes"]))),
			hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(partly.ContainerKeys, Is.Empty);
			Assert.That(fully.ContainerKeys, Is.EquivalentTo(ContainerKeys(site, "legacyTab")), "every copy of the merged tab");
		});
	}

	[Test]
	public void Hide_emptied_containers_merges_nested_groups_only_under_a_same_name_parent_tab()
	{
		// The General groups share alias and name, but their parent tabs have different names, so the backoffice shows
		// them in two tabs. The site's own "Legacy tab" is found in the site type, not by alias among all copies.
		IContentType legacySettings = Document("legacySettings")
			.NamedTab("legacyTab", "LegacyTab")
			.NamedGroup("legacyTab/general", "General", "siteTitle")
			.Build();
		IContentType site = Document("site")
			.NamedTab("legacyTab", "Legacy tab")
			.NamedGroup("legacyTab/general", "General", "siteNotes")
			.ComposedOf(legacySettings)
			.Build();

		HiddenFields separate = _resolver.Resolve(site, Rules(("legacySettings", Block(properties: ["siteTitle"]))), hideEmptiedContainers: true);

		// With the same parent tab name, the two General groups are one group that still shows siteNotes.
		IContentType sameNameSettings = Document("sameNameSettings")
			.NamedTab("legacyTab", "Legacy tab")
			.NamedGroup("legacyTab/general", "General", "siteTitle")
			.Build();
		IContentType sameNameSite = Document("sameNameSite")
			.NamedTab("legacyTab", "Legacy tab")
			.NamedGroup("legacyTab/general", "general", "siteNotes")
			.ComposedOf(sameNameSettings)
			.Build();
		HiddenFields merged = _resolver.Resolve(sameNameSite, Rules(("sameNameSettings", Block(properties: ["siteTitle"]))), hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(separate.ContainerKeys, Is.EquivalentTo(ContainerKeys(legacySettings, "legacyTab", "legacyTab/general")));
			Assert.That(merged.ContainerKeys, Is.Empty);
		});
	}

	[Test]
	public void The_list_overload_resolves_only_the_types_own_rules()
	{
		// The configuration analyzer checks each configured key against its own type this way.
		HiddenFields result = _resolver.Resolve(_article, [Block(properties: ["hideFromSitemap"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_article, "hideFromSitemap") }), "composed properties are part of the type's own structure");
			Assert.That(result.UnmatchedOnCompositions, Is.Empty);
		});
	}

	[Test]
	public void Each_composition_is_looked_up_once_and_a_cycle_ends_the_walk()
	{
		// page -> first, second; first -> second -> first, and second -> page: a diamond and two cycles. Umbraco refuses
		// such a graph, and its own CompositionPropertyGroups would recurse forever on it, so the types are mocks whose
		// structures are those of plain built types.
		IContentType firstStructure = Document("first").Group("firstGroup", "firstProperty").Build();
		IContentType secondStructure = Document("second").Group("secondGroup", "secondProperty").Build();
		IContentType pageStructure = Document("page").Group("pageGroup", "pageProperty").Build();

		var page = new Mock<IContentType>();
		var first = new Mock<IContentTypeComposition>();
		var second = new Mock<IContentTypeComposition>();
		Mimic(page, pageStructure, first.Object, second.Object);
		Mimic(first, firstStructure, second.Object);
		Mimic(second, secondStructure, first.Object, page.Object);

		var lookups = new List<string>();
		RuleBlockLookup rules = alias =>
		{
			lookups.Add(alias);
			return alias switch
			{
				"page" => [Block(properties: ["pageProperty"])],
				"first" => [Block(properties: ["firstProperty", "noSuchProperty"])],
				"second" => [Block(containers: ["secondGroup"])],
				_ => [],
			};
		};

		HiddenFields result = _resolver.Resolve(page.Object, rules, hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(CompositionClosure.Of(page.Object).Select(type => type.Alias), Is.EqualTo(new[] { "first", "second" }));
			Assert.That(lookups, Is.EquivalentTo(new[] { "page", "first", "second" }), "each alias once");
			Assert.That(
				result.PropertyTypeKeys,
				Is.EquivalentTo(new[] { PropertyKey(pageStructure, "pageProperty"), PropertyKey(firstStructure, "firstProperty"), PropertyKey(secondStructure, "secondProperty") }));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(secondStructure, "secondGroup") }));
			Assert.That(result.UnmatchedOnCompositions.Select(unmatched => unmatched.Composition.Alias), Is.EqualTo(new[] { "first" }));
		});
	}

	private static void Mimic<T>(Mock<T> mock, IContentType structure, params IContentTypeComposition[] compositions)
		where T : class, IContentTypeComposition
	{
		mock.SetupGet(type => type.Alias).Returns(structure.Alias);
		mock.SetupGet(type => type.Key).Returns(structure.Key);
		mock.SetupGet(type => type.ContentTypeComposition).Returns(compositions);
		mock.SetupGet(type => type.CompositionPropertyTypes).Returns(() => structure.CompositionPropertyTypes);
		mock.SetupGet(type => type.CompositionPropertyGroups).Returns(() => structure.CompositionPropertyGroups);
	}

	private static RuleBlockLookup Rules(params (string Alias, ContentTypeVisibilityOptions Block)[] entries)
		=> alias => entries
			.Where(entry => string.Equals(entry.Alias, alias, StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Block)
			.ToList();

	private static ContentTypeVisibilityOptions Block(List<string>? properties = null, List<string>? containers = null)
		=> new() { Properties = properties ?? [], Containers = containers ?? [] };
}
