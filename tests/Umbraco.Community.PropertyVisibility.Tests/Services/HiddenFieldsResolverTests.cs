using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

[TestFixture]
public sealed class HiddenFieldsResolverTests
{
	private HiddenFieldsResolver _resolver = null!;

	// landingPage
	// ├─ contentTab (tab)
	// │  └─ contentTab/main: title, bannerImage, relatedLinks
	// ├─ seoTab (tab, own property metaDescription)
	// │  ├─ seoTab/meta: metaKeywords, metaTitle
	// │  └─ seoTab/social: socialImage
	// ├─ settingsTab (tab)
	// │  ├─ settingsTab/general: hideFromNavigation
	// │  └─ settingsTab/advanced: cssClass
	// └─ notes (root-level group): notesText
	private IContentType _landingPage = null!;

	[SetUp]
	public void SetUp()
	{
		_resolver = new HiddenFieldsResolver();
		_landingPage = Document("landingPage")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bannerImage", "relatedLinks")
			.Tab("seoTab", "metaDescription")
			.Group("seoTab/meta", "metaKeywords", "metaTitle")
			.Group("seoTab/social", "socialImage")
			.Tab("settingsTab")
			.Group("settingsTab/general", "hideFromNavigation")
			.Group("settingsTab/advanced", "cssClass")
			.Group("notes", "notesText")
			.Build();
	}

	[Test]
	public void No_rules_returns_the_shared_empty_result()
	{
		HiddenFields result = _resolver.Resolve(_landingPage, [], hideEmptiedContainers: true);

		Assert.That(result, Is.SameAs(HiddenFields.Empty));
	}

	[Test]
	public void Blank_aliases_are_ignored_and_not_reported()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(properties: ["", "   ", null!], containers: [" "])],
			hideEmptiedContainers: true);

		Assert.That(result, Is.SameAs(HiddenFields.Empty));
	}

	[Test]
	public void Own_property_is_hidden()
	{
		HiddenFields result = _resolver.Resolve(_landingPage, [Rules(properties: ["bannerImage"])], hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_landingPage, "bannerImage") }));
			Assert.That(result.ContainerKeys, Is.Empty, "contentTab/main still shows title and relatedLinks");
			Assert.That(result.UnmatchedPropertyAliases, Is.Empty);
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
		});
	}

	[Test]
	public void Composed_property_is_hidden()
	{
		IContentType siteSettings = Document("siteSettings")
			.Tab("settingsTab")
			.Group("settingsTab/navigation", "hideFromSitemap", "navigationTitle")
			.Build();
		IContentType article = Document("article")
			.Group("content", "title")
			.ComposedOf(siteSettings)
			.Build();

		HiddenFields result = _resolver.Resolve(article, [Rules(properties: ["hideFromSitemap"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(siteSettings, "hideFromSitemap") }));
			Assert.That(result.ContainerKeys, Is.Empty);
			Assert.That(result.UnmatchedPropertyAliases, Is.Empty);
		});
	}

	[Test]
	public void Tab_alias_hides_the_tab_its_groups_and_all_their_properties()
	{
		HiddenFields result = _resolver.Resolve(_landingPage, [Rules(containers: ["seoTab"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "seoTab/social")));
			Assert.That(
				result.PropertyTypeKeys,
				Is.EquivalentTo(PropertyKeys(_landingPage, "metaDescription", "metaKeywords", "metaTitle", "socialImage")));
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
		});
	}

	[Test]
	public void Tab_slash_group_alias_hides_only_that_group()
	{
		HiddenFields result = _resolver.Resolve(_landingPage, [Rules(containers: ["settingsTab/advanced"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(_landingPage, "settingsTab/advanced") }));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_landingPage, "cssClass") }));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(_landingPage, "settingsTab")));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(_landingPage, "settingsTab/general")));
		});
	}

	[Test]
	public void Group_local_alias_alone_does_not_match_a_nested_group()
	{
		// "advanced" is only the local part of "settingsTab/advanced"; the grammar requires the full path.
		HiddenFields result = _resolver.Resolve(_landingPage, [Rules(containers: ["advanced"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.Empty);
			Assert.That(result.UnmatchedContainerAliases, Is.EqualTo(new[] { "advanced" }));
		});
	}

	[Test]
	public void Root_level_group_alias_hides_that_group()
	{
		HiddenFields result = _resolver.Resolve(_landingPage, [Rules(containers: ["notes"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(_landingPage, "notes") }));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(_landingPage, "notesText") }));
		});
	}

	[Test]
	public void Container_aliases_match_case_insensitively()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(containers: ["SeoTab", "SETTINGSTAB/Advanced", "NOTES"])],
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(
				result.ContainerKeys,
				Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "seoTab/social", "settingsTab/advanced", "notes")));
			Assert.That(
				result.PropertyTypeKeys,
				Is.EquivalentTo(PropertyKeys(_landingPage, "metaDescription", "metaKeywords", "metaTitle", "socialImage", "cssClass", "notesText")));
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
		});
	}

	[Test]
	public void Property_aliases_match_case_insensitively()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(properties: ["BANNERIMAGE", "metakeywords"])],
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "bannerImage", "metaKeywords")));
			Assert.That(result.UnmatchedPropertyAliases, Is.Empty);
		});
	}

	[Test]
	public void Unknown_aliases_are_reported_and_known_ones_still_hidden()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[
				Rules(properties: ["bannerImage", "noSuchProperty"], containers: ["noSuchTab", "seoTab/noSuchGroup", "notes"]),
				Rules(properties: ["NOSUCHPROPERTY"], containers: [" noSuchTab "]),
			],
			hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.UnmatchedPropertyAliases, Is.EqualTo(new[] { "noSuchProperty" }), "reported once, trimmed, first spelling kept");
			Assert.That(result.UnmatchedContainerAliases, Is.EqualTo(new[] { "noSuchTab", "seoTab/noSuchGroup" }));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "bannerImage", "notesText")));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(_landingPage, "notes") }));
		});
	}

	[Test]
	public void Same_alias_groups_from_two_compositions_are_all_returned()
	{
		IContentType siteSettings = Document("siteSettings")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaKeywords")
			.Build();
		IContentType pageSettings = Document("pageSettings")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaTitle")
			.Build();
		IContentType article = Document("article")
			.Tab("seoTab", "metaDescription")
			.Group("content", "title")
			.ComposedOf(siteSettings)
			.ComposedOf(pageSettings)
			.Build();

		Guid[] metaGroups = ContainerKeys(article, "seoTab/meta");
		Guid[] seoTabs = ContainerKeys(article, "seoTab");
		Assert.That(metaGroups, Has.Length.EqualTo(2), "precondition: both composition copies survive CompositionPropertyGroups");
		Assert.That(seoTabs, Has.Length.EqualTo(3), "precondition: own tab plus both composition copies");

		HiddenFields groupResult = _resolver.Resolve(article, [Rules(containers: ["seoTab/meta"])], hideEmptiedContainers: false);
		HiddenFields tabResult = _resolver.Resolve(article, [Rules(containers: ["seoTab"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(groupResult.ContainerKeys, Is.EquivalentTo(metaGroups));
			Assert.That(groupResult.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(article, "metaKeywords", "metaTitle")));

			Assert.That(tabResult.ContainerKeys, Is.EquivalentTo(seoTabs.Concat(metaGroups)));
			Assert.That(tabResult.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(article, "metaDescription", "metaKeywords", "metaTitle")));
			Assert.That(tabResult.ContainerKeys, Does.Not.Contain(ContainerKey(article, "content")));
		});
	}

	[Test]
	public void Overlapping_rules_across_blocks_yield_no_duplicate_keys()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[
				Rules(properties: ["metaKeywords", "bannerImage"], containers: ["seoTab/meta"]),
				Rules(properties: ["METAKEYWORDS", "bannerImage"], containers: ["seoTab", "seoTab/meta", "SEOTAB"]),
			],
			hideEmptiedContainers: true);

		Guid[] expectedProperties = PropertyKeys(_landingPage, "bannerImage", "metaDescription", "metaKeywords", "metaTitle", "socialImage");
		Guid[] expectedContainers = ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "seoTab/social");

		Assert.Multiple(() =>
		{
			Assert.That(result.PropertyTypeKeys, Has.Count.EqualTo(expectedProperties.Length));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(expectedProperties));
			Assert.That(result.ContainerKeys, Has.Count.EqualTo(expectedContainers.Length));
			Assert.That(result.ContainerKeys, Is.EquivalentTo(expectedContainers));
			Assert.That(result.PropertyTypeKeys.ToArray(), Is.Unique);
			Assert.That(result.ContainerKeys.ToArray(), Is.Unique);
		});
	}

	[Test]
	public void Element_types_are_resolved_like_document_types()
	{
		IContentType promoBanner = Element("promoBanner")
			.Group("content", "headline", "overlayColour")
			.Build();
		IContentType promoBannerSettings = Element("promoBannerSettings")
			.Group("settings", "anchorId")
			.Build();

		HiddenFields bannerResult = _resolver.Resolve(promoBanner, [Rules(properties: ["overlayColour"])], hideEmptiedContainers: true);
		HiddenFields settingsResult = _resolver.Resolve(promoBannerSettings, [Rules(properties: ["anchorId"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(promoBanner.IsElement, Is.True);
			Assert.That(bannerResult.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(promoBanner, "overlayColour") }));
			Assert.That(bannerResult.ContainerKeys, Is.Empty);
			Assert.That(settingsResult.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(promoBannerSettings, "anchorId") }));
			Assert.That(settingsResult.ContainerKeys, Is.Empty);
		});
	}

	[Test]
	public void Hide_emptied_containers_adds_a_group_whose_properties_are_all_hidden()
	{
		ContentTypeVisibilityOptions rules = Rules(properties: ["cssClass"]);

		HiddenFields on = _resolver.Resolve(_landingPage, [rules], hideEmptiedContainers: true);
		HiddenFields off = _resolver.Resolve(_landingPage, [rules], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(on.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(_landingPage, "settingsTab/advanced") }), "settingsTab keeps the visible general group");
			Assert.That(off.ContainerKeys, Is.Empty);
			Assert.That(on.PropertyTypeKeys, Is.EquivalentTo(off.PropertyTypeKeys));
		});
	}

	[Test]
	public void Hide_emptied_containers_adds_a_tab_whose_groups_and_own_properties_are_all_hidden()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(properties: ["metaDescription", "metaKeywords", "metaTitle", "socialImage"])],
			hideEmptiedContainers: true);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "seoTab/social")));
	}

	[Test]
	public void Hide_emptied_containers_adds_a_tab_emptied_by_a_mix_of_container_and_property_rules()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(containers: ["seoTab/meta"]), Rules(properties: ["metaDescription", "socialImage"])],
			hideEmptiedContainers: true);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "seoTab/social")));
	}

	[Test]
	public void Hide_emptied_containers_keeps_a_tab_with_a_visible_own_property()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(properties: ["metaKeywords", "metaTitle", "socialImage"])],
			hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab/meta", "seoTab/social")));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(_landingPage, "seoTab")), "metaDescription on the tab is still visible");
		});
	}

	[Test]
	public void Hide_emptied_containers_keeps_a_tab_with_a_visible_group()
	{
		HiddenFields result = _resolver.Resolve(
			_landingPage,
			[Rules(properties: ["metaDescription", "metaKeywords", "metaTitle"])],
			hideEmptiedContainers: true);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(_landingPage, "seoTab/meta") }));
	}

	[Test]
	public void Hide_emptied_containers_never_adds_intrinsically_empty_containers()
	{
		IContentType contentType = Document("landingPage")
			.Tab("emptyTab")
			.Tab("placeholderTab")
			.Group("placeholderTab/placeholder")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bannerImage")
			.Group("emptyGroup")
			.Build();

		HiddenFields result = _resolver.Resolve(contentType, [Rules(properties: ["title"])], hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.Empty, "contentTab/main still shows bannerImage");
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(contentType, "emptyTab")));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(contentType, "emptyGroup")));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(contentType, "placeholderTab")), "nothing was taken from it");
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(contentType, "placeholderTab/placeholder")));
		});
	}

	[Test]
	public void Hide_emptied_containers_hides_a_tab_whose_only_remaining_group_has_no_properties()
	{
		// The backoffice renders every child group of a tab as a box, with or without properties, so leaving the tab
		// would show a tab holding one empty headed box.
		IContentType contentType = Document("landingPage")
			.Tab("contentTab")
			.Group("contentTab/placeholder")
			.Group("contentTab/main", "title")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaTitle")
			.Build();

		HiddenFields result = _resolver.Resolve(contentType, [Rules(properties: ["title"])], hideEmptiedContainers: true);

		Assert.That(
			result.ContainerKeys,
			Is.EquivalentTo(ContainerKeys(contentType, "contentTab", "contentTab/main", "contentTab/placeholder")),
			"the empty placeholder group goes with its tab; seoTab is untouched");
	}

	[Test]
	public void Hide_emptied_containers_evaluates_same_alias_copies_from_compositions_as_one_container()
	{
		IContentType siteSettings = Document("siteSettings")
			.Group("seo", "metaKeywords")
			.Build();
		IContentType article = Document("article")
			.Group("seo", "metaTitle")
			.Group("content", "title")
			.ComposedOf(siteSettings)
			.Build();

		Guid[] seoCopies = ContainerKeys(article, "seo");
		Assert.That(seoCopies, Has.Length.EqualTo(2), "precondition: own group and composition copy");

		HiddenFields partly = _resolver.Resolve(article, [Rules(properties: ["metaKeywords"])], hideEmptiedContainers: true);
		HiddenFields fully = _resolver.Resolve(article, [Rules(properties: ["metaKeywords", "metaTitle"])], hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(partly.ContainerKeys, Is.Empty, "the merged seo group still shows metaTitle");
			Assert.That(fully.ContainerKeys, Is.EquivalentTo(seoCopies));
		});
	}

	// mediaPage, as Umbraco 17 stores a container name with a slash (alias = tab alias + "/" + group alias):
	// ├─ media (tab "Media")
	// │  ├─ media/details (group "Details"): caption
	// │  └─ media/image/Video (group "Image/Video"): coverImage
	// └─ other (tab): otherText
	private static IContentType MediaPage() => Document("mediaPage")
		.NamedTab("media", "Media")
		.NamedGroup("media/details", "Details", "caption")
		.NamedGroup("media/image/Video", "Image/Video", "coverImage")
		.Tab("other", "otherText")
		.Build();

	[Test]
	public void A_tab_rule_hides_a_group_whose_name_holds_a_slash()
	{
		// The Management API gives media/image/Video the parent media (the first segment), so the backoffice renders it in
		// that tab and removes it with the tab.
		IContentType mediaPage = MediaPage();

		HiddenFields result = _resolver.Resolve(mediaPage, [Rules(containers: ["media"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(mediaPage, "media", "media/details", "media/image/Video")));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(mediaPage, "caption", "coverImage")));
			Assert.That(result.UnmatchedContainerAliases, Is.Empty);
		});
	}

	[Test]
	public void A_group_whose_name_holds_a_slash_is_addressed_by_its_full_alias()
	{
		IContentType mediaPage = MediaPage();

		HiddenFields result = _resolver.Resolve(mediaPage, [Rules(containers: ["MEDIA/image/video"])], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(mediaPage, "media/image/Video") }));
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(mediaPage, "coverImage") }));
		});
	}

	[Test]
	public void Hide_emptied_containers_keeps_a_tab_whose_group_with_a_slash_in_its_name_is_still_visible()
	{
		// Before the parent was taken from the first segment, Image/Video did not count as a child of media: hiding caption
		// emptied the only child the resolver saw, the tab was hidden, and removing it took coverImage with it.
		IContentType mediaPage = MediaPage();

		HiddenFields result = _resolver.Resolve(mediaPage, [Rules(properties: ["caption"])], hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(mediaPage, "media/details") }));
			Assert.That(result.ContainerKeys, Does.Not.Contain(ContainerKey(mediaPage, "media")), "coverImage is still visible in it");
			Assert.That(result.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(mediaPage, "caption") }));
		});
	}

	[Test]
	public void Hide_emptied_containers_hides_a_tab_once_its_group_with_a_slash_in_its_name_is_emptied_too()
	{
		IContentType mediaPage = MediaPage();

		HiddenFields result = _resolver.Resolve(mediaPage, [Rules(properties: ["caption", "coverImage"])], hideEmptiedContainers: true);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(mediaPage, "media", "media/details", "media/image/Video")));
	}

	[Test]
	public void A_group_whose_first_alias_segment_names_no_container_is_outside_any_tab()
	{
		// Tab "Header/Footer" has the alias header/Footer; its group "Links" gets header/Footer/links. The Management API
		// looks up the parent "header", finds none, and reports the group without a parent, so the backoffice does not
		// render it in the tab and does not remove it with the tab.
		IContentType page = Document("page")
			.NamedTab("header/Footer", "Header/Footer", "footerText")
			.NamedGroup("header/Footer/links", "Links", "link")
			.Build();

		HiddenFields tab = _resolver.Resolve(page, [Rules(containers: ["header/Footer"])], hideEmptiedContainers: true);
		HiddenFields group = _resolver.Resolve(page, [Rules(containers: ["header/Footer/links"])], hideEmptiedContainers: true);

		Assert.Multiple(() =>
		{
			Assert.That(tab.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(page, "header/Footer") }));
			Assert.That(tab.PropertyTypeKeys, Is.EquivalentTo(new[] { PropertyKey(page, "footerText") }));
			Assert.That(group.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(page, "header/Footer/links") }));
			Assert.That(group.ContainerKeys, Does.Not.Contain(ContainerKey(page, "header/Footer")), "footerText keeps the tab visible");
		});
	}

	[Test]
	public void A_root_level_group_whose_first_alias_segment_is_a_tab_belongs_to_that_tab()
	{
		// A root-level group named "Image/Video" has the alias image/Video; with a tab aliased image, the Management API
		// reports that tab as its parent.
		IContentType page = Document("page")
			.NamedTab("image", "Image", "altText")
			.NamedGroup("image/Video", "Image/Video", "videoUrl")
			.Build();

		HiddenFields result = _resolver.Resolve(page, [Rules(properties: ["altText", "videoUrl"])], hideEmptiedContainers: true);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(ContainerKeys(page, "image", "image/Video")), "the emptied group and its tab");
	}

	[Test]
	public void The_parent_is_looked_up_case_sensitively_as_the_Management_API_does()
	{
		IContentType page = Document("page")
			.NamedTab("media", "Media")
			.NamedGroup("Media/gallery", "Gallery", "images")
			.Build();

		HiddenFields result = _resolver.Resolve(page, [Rules(containers: ["media"])], hideEmptiedContainers: false);

		Assert.That(result.ContainerKeys, Is.EquivalentTo(new[] { ContainerKey(page, "media") }), "Media/gallery has no parent: 'Media' is not the alias 'media'");
	}

	[Test]
	public void Configurable_container_aliases_leave_out_aliases_a_rule_cannot_use()
	{
		IContentType page = Document("page")
			.Tab("seoTab")
			.NamedGroup("seoTab/meta", "Meta", "metaTitle")
			.NamedGroup("/extras", "/extras", "extra")
			.NamedGroup("media/image/Video", "Image/Video", "coverImage")
			.Build();

		Assert.That(
			HiddenFieldsResolver.ConfigurableContainerAliases(page),
			Is.EqualTo(new[] { "media/image/Video", "seoTab", "seoTab/meta" }),
			"'/extras' fails validation (PV006), so it is never listed or suggested");
	}

	private static ContentTypeVisibilityOptions Rules(List<string>? properties = null, List<string>? containers = null)
		=> new() { Properties = properties ?? [], Containers = containers ?? [] };
}
