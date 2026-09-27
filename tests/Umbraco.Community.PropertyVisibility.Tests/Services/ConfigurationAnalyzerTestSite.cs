using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Configuration;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Services;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     An in-memory site for <see cref="ConfigurationAnalyzer" />: the multi-site sample configuration, two root nodes
///     ("Corporate site", "Campaign site") and the sample document and element types, with the real
///     <see cref="SiteMatcher" /> and <see cref="HiddenFieldsResolver" />. Navigation, root names, content types, the
///     configuration info and the Umbraco version are mocks whose data the tests change through the public members.
/// </summary>
internal sealed class ConfigurationAnalyzerTestSite
{
	public const string FilePath = "/site/PropertyVisibility.config.json";
	public const string SampleRulesHash = "76d8e2ea0c1b5a4f9e3d2c1b0a9f8e7d6c5b4a3f2e1d0c9b8a7f6e5d4c3b2a19";

	public static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	public static readonly Guid CampaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");
	public static readonly Guid OtherRoot = Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");
	public static readonly Guid CorporateSection = Guid.Parse("1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b");
	public static readonly Guid TrashedPage = Guid.Parse("3c4d5e6f-7a8b-4c9d-8e0f-2a3b4c5d6e7f");
	public static readonly Guid UnknownKey = Guid.Parse("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e");
	public static readonly DateTimeOffset LoadedAt = new(2026, 9, 25, 14, 48, 35, TimeSpan.Zero);

	private readonly Mock<IOptionsMonitor<PropertyVisibilityOptions>> _monitor = new();
	private readonly Mock<IConfigurationInfo> _info = new();
	private readonly Mock<IDocumentNavigationQueryService> _navigation = new();
	private readonly Mock<IRootNodeResolver> _rootNodeResolver = new();
	private readonly Mock<IContentTypeService> _contentTypeService = new();
	private readonly Mock<IUmbracoVersion> _umbracoVersion = new();
	private readonly Dictionary<Guid, string> _rootNames = [];

	public ConfigurationAnalyzerTestSite()
	{
		SiteSettings = Document("siteSettings")
			.Tab("legacyTab")
			.Group("legacyTab/legacy", "legacyNotes")
			.Build();
		LandingPage = Document("landingPage")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bannerImage", "relatedLinks")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaKeywords", "metaTitle")
			.Tab("settingsTab")
			.Group("settingsTab/advanced", "cssClass", "anchorId")
			.ComposedOf(SiteSettings)
			.Build();
		Article = Document("article")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bodyText")
			.Tab("shareTab")
			.Group("shareTab/social", "shareImage")
			.Build();
		PromoBanner = Element("promoBanner").Group("main", "headline", "overlayColour").Build();
		PromoBannerSettings = Element("promoBannerSettings").Group("settings", "anchorId").Build();
		CallToAction = Element("callToAction")
			.Tab("contentTab")
			.Group("contentTab/main", "label", "link")
			.Tab("settingsTab")
			.Group("settingsTab/display", "style")
			.Build();
		ContentTypes = [SiteSettings, LandingPage, Article, PromoBanner, PromoBannerSettings, CallToAction];

		Options = Sample();
		State = new ConfigurationState(ConfigurationSource.Appsettings, FilePath, LoadedAt, SampleRulesHash, []);
		UmbracoVersion = new Version(17, 6, 2);

		_monitor.SetupGet(monitor => monitor.CurrentValue).Returns(() =>
		{
			Reads.Add("options");
			return OptionsFailure is null ? Options : throw OptionsFailure;
		});
		_info.SetupGet(info => info.Current).Returns(() =>
		{
			Reads.Add("configurationInfo");
			return State;
		});
		_contentTypeService.Setup(service => service.GetAll()).Returns(() => ContentTypes);
		_umbracoVersion.SetupGet(version => version.Version).Returns(() => UmbracoVersion);
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(It.IsAny<Guid>())).Returns((Guid key) => _rootNames.GetValueOrDefault(key));
		_rootNodeResolver.Setup(resolver => resolver.IsRoot(It.IsAny<Guid>())).Returns((Guid key) => _rootNames.ContainsKey(key));
		_rootNodeResolver.Setup(resolver => resolver.Resolve(It.IsAny<Guid>(), null)).Returns(RootNodeResolution.None);
		_rootNodeResolver
			.Setup(resolver => resolver.Resolve(CorporateSection, null))
			.Returns(new RootNodeResolution(CorporateRoot, RootResolutionSource.Document));
		_rootNodeResolver.Setup(resolver => resolver.Resolve(TrashedPage, null)).Returns(RootNodeResolution.RecycleBin);

		Roots((CorporateRoot, "Corporate site"), (CampaignRoot, "Campaign site"));
	}

	public IContentType SiteSettings { get; }

	public IContentType LandingPage { get; }

	public IContentType Article { get; }

	public IContentType PromoBanner { get; }

	public IContentType PromoBannerSettings { get; }

	public IContentType CallToAction { get; }

	/// <summary>The content types <see cref="IContentTypeService.GetAll" /> returns.</summary>
	public List<IContentType> ContentTypes { get; set; }

	/// <summary>The options the monitor returns, unless <see cref="OptionsFailure" /> is set.</summary>
	public PropertyVisibilityOptions Options { get; set; }

	/// <summary>When set, reading the options throws this.</summary>
	public Exception? OptionsFailure { get; set; }

	/// <summary>What <see cref="IConfigurationInfo.Current" /> returns.</summary>
	public ConfigurationState State { get; set; }

	/// <summary>The running Umbraco version.</summary>
	public Version UmbracoVersion { get; set; }

	/// <summary>The order in which the options and the configuration info were read.</summary>
	public List<string> Reads { get; } = [];

	/// <summary>The content type service mock, for call verification.</summary>
	public Mock<IContentTypeService> ContentTypeService => _contentTypeService;

	/// <summary>The multi-site sample of the configuration reference, as options.</summary>
	public static PropertyVisibilityOptions Sample() => new()
	{
		ContentTypes = { ["siteSettings"] = Block(containers: ["legacyTab"]) },
		Sites =
		{
			["corporate"] = new SiteVisibilityOptions
			{
				RootNodeKey = CorporateRoot,
				RootNodeName = "Corporate site",
				ContentTypes =
				{
					["landingPage"] = Block(["bannerImage", "relatedLinks"], ["seoTab", "settingsTab/advanced"]),
					["article"] = Block(containers: ["shareTab"]),
					["promoBanner"] = Block(["overlayColour"]),
					["promoBannerSettings"] = Block(["anchorId"]),
				},
			},
			["campaign"] = new SiteVisibilityOptions
			{
				RootNodeName = "Campaign site",
				ContentTypes =
				{
					["landingPage"] = Block(["metaKeywords"]),
					["callToAction"] = Block(containers: ["settingsTab"]),
				},
			},
			["everythingElse"] = new SiteVisibilityOptions
			{
				IsDefault = true,
				ContentTypes = { ["landingPage"] = Block(["bannerImage"]) },
			},
		},
	};

	/// <summary>A rule block.</summary>
	public static ContentTypeVisibilityOptions Block(string[]? properties = null, string[]? containers = null)
		=> new() { Properties = [.. properties ?? []], Containers = [.. containers ?? []] };

	/// <summary>Replaces the root nodes of the content tree (in tree order) and their names.</summary>
	public void Roots(params (Guid Key, string Name)[] roots)
	{
		_rootNames.Clear();
		foreach ((Guid key, var name) in roots)
		{
			_rootNames[key] = name;
		}

		IEnumerable<Guid> keys = roots.Select(root => root.Key).ToArray();
		_navigation.Setup(navigation => navigation.TryGetRootKeys(out keys)).Returns(true);
	}

	/// <summary>
	///     Marks an own property of a content type as mandatory. <see cref="IContentTypeComposition.CompositionPropertyTypes" />
	///     returns clones of composed properties, so a composed property is marked on the composition.
	/// </summary>
	public static void Mandatory(IContentType contentType, string alias)
		=> contentType.PropertyTypes.Single(property => property.Alias == alias).Mandatory = true;

	/// <summary>An analyzer over this site; <paramref name="monitor" /> replaces the mocked options monitor.</summary>
	public ConfigurationAnalyzer CreateAnalyzer(IOptionsMonitor<PropertyVisibilityOptions>? monitor = null)
		=> new(
			monitor ?? _monitor.Object,
			_info.Object,
			_navigation.Object,
			_rootNodeResolver.Object,
			new SiteMatcher(_rootNodeResolver.Object),
			_contentTypeService.Object,
			new HiddenFieldsResolver(),
			_umbracoVersion.Object);
}
