using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     The service with the real <see cref="SiteMatcher" /> and <see cref="HiddenFieldsResolver" />; only the options
///     monitor, the content type service and the root node resolver (navigation and content IO) are mocked.
/// </summary>
[TestFixture]
public sealed class PropertyVisibilityServiceTests
{
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid CampaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");
	private static readonly Guid OtherRoot = Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");
	private static readonly Guid YetAnotherRoot = Guid.Parse("8b9c0d1e-2f3a-4b4c-9d5e-6f7a8b9c0d1e");

	private static readonly Guid CorporatePage = Guid.Parse("11111111-1111-4111-8111-111111111111");
	private static readonly Guid CampaignPage = Guid.Parse("22222222-2222-4222-8222-222222222222");
	private static readonly Guid OtherPage = Guid.Parse("33333333-3333-4333-8333-333333333333");
	private static readonly Guid YetAnotherPage = Guid.Parse("44444444-4444-4444-8444-444444444444");
	private static readonly Guid TrashedPage = Guid.Parse("55555555-5555-4555-8555-555555555555");
	private static readonly Guid NewPageAtRoot = Guid.Parse("66666666-6666-4666-8666-666666666666");

	private PropertyVisibilityOptions _options = null!;
	private Exception? _optionsFailure;
	private Action<PropertyVisibilityOptions, string?>? _onChange;
	private Mock<IDisposable> _changeSubscription = null!;
	private Mock<IContentTypeService> _contentTypeService = null!;
	private Mock<IRootNodeResolver> _rootNodeResolver = null!;
	private ListLogger<PropertyVisibilityService> _logger = null!;
	private IContentType _landingPage = null!;
	private PropertyVisibilityService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_options = SampleOptions();
		_optionsFailure = null;
		_onChange = null;

		var monitor = new Mock<IOptionsMonitor<PropertyVisibilityOptions>>();
		monitor.SetupGet(m => m.CurrentValue).Returns(() => _optionsFailure is null ? _options : throw _optionsFailure);
		_changeSubscription = new Mock<IDisposable>();
		monitor
			.Setup(m => m.OnChange(It.IsAny<Action<PropertyVisibilityOptions, string?>>()))
			.Callback<Action<PropertyVisibilityOptions, string?>>(listener => _onChange = listener)
			.Returns(_changeSubscription.Object);

		_landingPage = Document("landingPage")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bannerImage", "relatedLinks")
			.Tab("seoTab")
			.Group("seoTab/meta", "metaKeywords", "metaTitle")
			.Tab("settingsTab")
			.Group("settingsTab/advanced", "cssClass", "anchorId")
			.Build();

		_contentTypeService = new Mock<IContentTypeService>();
		_contentTypeService.Setup(service => service.Get(_landingPage.Key)).Returns(_landingPage);

		_rootNodeResolver = new Mock<IRootNodeResolver>();
		Resolves(CorporatePage, CorporateRoot);
		Resolves(CampaignPage, CampaignRoot);
		Resolves(OtherPage, OtherRoot);
		Resolves(YetAnotherPage, YetAnotherRoot);
		_rootNodeResolver.Setup(resolver => resolver.Resolve(TrashedPage, It.IsAny<Guid?>())).Returns(RootNodeResolution.RecycleBin);
		_rootNodeResolver.Setup(resolver => resolver.Resolve(NewPageAtRoot, It.IsAny<Guid?>())).Returns(RootNodeResolution.None);
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(CorporateRoot)).Returns("Corporate site");
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(CampaignRoot)).Returns("Campaign site");
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(OtherRoot)).Returns("Some other site");
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(YetAnotherRoot)).Returns("Yet another site");

		_logger = new ListLogger<PropertyVisibilityService>();
		_service = new PropertyVisibilityService(
			monitor.Object,
			_contentTypeService.Object,
			_rootNodeResolver.Object,
			new SiteMatcher(_rootNodeResolver.Object),
			new HiddenFieldsResolver(),
			_logger);
	}

	[TearDown]
	public void TearDown() => _service.Dispose();

	[Test]
	public void Disabled_package_returns_the_disabled_response_without_any_lookup()
	{
		_options.Enabled = false;

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.Disabled, Is.True);
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.ContainerKeys, Is.Empty);
			Assert.That(response.MatchedSite, Is.Null);
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.Disabled));
		});
		_contentTypeService.VerifyNoOtherCalls();
		_rootNodeResolver.Verify(resolver => resolver.Resolve(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
	}

	[Test]
	public void Unknown_content_type_returns_an_empty_response()
	{
		var unknownType = Guid.NewGuid();

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, unknownType, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.Disabled, Is.False);
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.ContainerKeys, Is.Empty);
			Assert.That(response.RootResolution, Is.EqualTo(RootResolutionSource.None));
			Assert.That(response.MatchedSite, Is.Null);
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.UnknownContentTypeKey).And.Contains(unknownType.ToString()));
		});
		_rootNodeResolver.Verify(resolver => resolver.Resolve(It.IsAny<Guid>(), It.IsAny<Guid?>()), Times.Never);
	}

	[Test]
	public void Invalid_options_fail_open_and_each_distinct_failure_is_logged_once()
	{
		_optionsFailure = new OptionsValidationException(
			Options.DefaultName,
			typeof(PropertyVisibilityOptions),
			["PV003 (Sites:corporate): Site 'corporate' has none of RootNodeKey, RootNodeName or IsDefault."]);

		HiddenFieldsResponseModel first = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		HiddenFieldsResponseModel second = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			foreach (HiddenFieldsResponseModel response in new[] { first, second })
			{
				Assert.That(response.Disabled, Is.False);
				Assert.That(response.PropertyTypeKeys, Is.Empty);
				Assert.That(response.ContainerKeys, Is.Empty);
				Assert.That(response.MatchedSite, Is.Null);
				Assert.That(response.Warnings, Has.Count.EqualTo(1));
				Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.ConfigurationInvalid));
			}

			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.StartWith(IssueCodes.ConfigurationInvalid).And.Contain("PV003"));
			Assert.That(_logger.At(LogLevel.Error)[0].Exception, Is.SameAs(_optionsFailure));
		});
		_contentTypeService.VerifyNoOtherCalls();

		// A different failure replaces the first one without a valid configuration in between (the options monitor does
		// not notify change listeners for invalid options): it is logged too.
		_optionsFailure = new InvalidOperationException("Failed to convert configuration value 'nope' at 'PropertyVisibility:Enabled' to type 'System.Boolean'.");
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(2), "a different failure is logged once");
		Assert.That(_logger.At(LogLevel.Error)[1].Message, Does.Contain("PropertyVisibility:Enabled"));

		// A valid read re-arms the log: the same failure coming back later is logged again.
		_optionsFailure = null;
		HiddenFieldsResponseModel valid = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		Assert.That(valid.PropertyTypeKeys, Is.Not.Empty);

		_optionsFailure = new InvalidOperationException("Failed to convert configuration value 'nope' at 'PropertyVisibility:Enabled' to type 'System.Boolean'.");
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(3), "the failure returned after a valid configuration");
	}

	[Test]
	public void Any_exception_while_reading_the_options_fails_open()
	{
		_optionsFailure = new FormatException("unexpected");

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.ConfigurationInvalid));
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Unexpected_failure_fails_open_and_is_logged()
	{
		var failure = new InvalidOperationException("navigation structure not ready");
		_rootNodeResolver.Setup(resolver => resolver.Resolve(CorporatePage, It.IsAny<Guid?>())).Throws(failure);

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.Disabled, Is.False);
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.ContainerKeys, Is.Empty);
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Error)[0].Exception, Is.SameAs(failure));
		});
	}

	[Test]
	public void A_repeated_unexpected_failure_is_logged_as_an_error_once_and_then_at_debug_until_the_options_change()
	{
		_rootNodeResolver.Setup(resolver => resolver.Resolve(CorporatePage, It.IsAny<Guid?>())).Throws(new InvalidOperationException("navigation structure not ready"));
		_rootNodeResolver.Setup(resolver => resolver.Resolve(CampaignPage, It.IsAny<Guid?>())).Throws(new InvalidOperationException("something else"));

		for (var request = 0; request < 3; request++)
		{
			_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		}

		_service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(2), "one per distinct exception type and message");
			Assert.That(_logger.At(LogLevel.Error).Select(entry => entry.Exception!.Message), Is.EqualTo(new[] { "navigation structure not ready", "something else" }));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.Contain("Repeats of this failure are logged at Debug level"));
			Assert.That(RepeatedFailureLogs(), Has.Count.EqualTo(2));
			Assert.That(RepeatedFailureLogs()[0].Message, Does.Contain("System.InvalidOperationException: navigation structure not ready"));
			Assert.That(RepeatedFailureLogs().Select(entry => entry.Exception), Is.All.Null, "no stack trace for a repeat");
		});

		// A configuration change starts over.
		_onChange!(_options, Options.DefaultName);
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(3));
	}

	[Test]
	public void The_same_message_from_another_exception_type_is_a_distinct_failure()
	{
		_rootNodeResolver.Setup(resolver => resolver.Resolve(CorporatePage, It.IsAny<Guid?>())).Throws(new InvalidOperationException("not ready"));
		_rootNodeResolver.Setup(resolver => resolver.Resolve(CampaignPage, It.IsAny<Guid?>())).Throws(new TimeoutException("not ready"));

		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		_service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);

		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(2));
	}

	[Test]
	public void Global_and_key_matched_site_rules_are_unioned()
	{
		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(
				response.PropertyTypeKeys,
				Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "bannerImage", "metaKeywords", "metaTitle", "cssClass", "anchorId")));
			Assert.That(response.PropertyTypeKeys, Is.Unique);
			Assert.That(
				response.ContainerKeys,
				Is.EquivalentTo(ContainerKeys(_landingPage, "seoTab", "seoTab/meta", "settingsTab/advanced", "settingsTab")),
				"settingsTab only holds the hidden advanced group, so it is emptied too");
			Assert.That(response.RootResolution, Is.EqualTo(RootResolutionSource.Document));
			Assert.That(response.MatchedSite, Is.Not.Null);
			Assert.That(response.MatchedSite!.Label, Is.EqualTo("corporate"));
			Assert.That(response.MatchedSite.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(response.Warnings, Is.Empty);
			Assert.That(response.Disabled, Is.False);
		});
		AssertNoRootIdentifiers(response);
	}

	[Test]
	public void Matched_site_exposes_only_its_label_and_reason()
	{
		// By design: any Content section user can call the API, so the response must not map a document to its site's
		// root node key or name.
		var properties = typeof(MatchedSiteModel).GetProperties().Select(property => property.Name);

		Assert.That(properties, Is.EquivalentTo(new[] { nameof(MatchedSiteModel.Label), nameof(MatchedSiteModel.Reason) }));
	}

	[Test]
	public void The_serialized_response_carries_no_root_key_or_name()
	{
		HiddenFieldsResponseModel byKey = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		HiddenFieldsResponseModel byName = _service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);

		foreach (HiddenFieldsResponseModel response in new[] { byKey, byName })
		{
			var json = System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

			Assert.Multiple(() =>
			{
				Assert.That(json, Does.Not.Contain("rootNodeKey").IgnoreCase);
				Assert.That(json, Does.Not.Contain("rootNodeName").IgnoreCase);
				Assert.That(json, Does.Contain("\"matchedSite\":{\"label\":"));
			});
			AssertNoRootIdentifiers(response);
		}
	}

	[Test]
	public void Global_and_name_matched_site_rules_are_unioned()
	{
		HiddenFieldsResponseModel response = _service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "metaKeywords")));
			Assert.That(response.ContainerKeys, Is.Empty);
			Assert.That(response.MatchedSite!.Label, Is.EqualTo("campaign"));
			Assert.That(response.MatchedSite.Reason, Is.EqualTo(SiteMatchReason.Name));
			Assert.That(response.Warnings, Is.Empty);
		});
	}

	[Test]
	public void The_rule_sets_a_site_includes_are_united_with_its_own_and_the_global_rules()
	{
		_options.RuleSets["simplePages"] = new RuleSetOptions
		{
			ContentTypes = { ["LandingPage"] = new ContentTypeVisibilityOptions { Properties = ["title"] } },
		};
		_options.RuleSets["notIncluded"] = new RuleSetOptions
		{
			ContentTypes = { ["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["metaTitle"] } },
		};
		_options.Sites["campaign"].Include = ["SimplePages", "simplePages"];

		HiddenFieldsResponseModel campaign = _service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);
		HiddenFieldsResponseModel other = _service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			// Global relatedLinks, campaign's own metaKeywords, and title from the rule set it includes.
			Assert.That(campaign.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "metaKeywords", "title")));
			Assert.That(campaign.MatchedSite!.Label, Is.EqualTo("campaign"));
			Assert.That(campaign.Warnings, Is.Empty);
			Assert.That(other.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "bannerImage")), "the default site includes no rule set");
		});
	}

	[Test]
	public void A_rule_set_entry_keyed_by_a_composition_applies_to_the_composing_type_on_the_including_site_only()
	{
		IContentType siteSettings = Document("siteSettings")
			.Tab("legacyTab")
			.Group("legacyTab/general", "siteTitle")
			.Group("analytics", "trackingCode", "tagManagerId")
			.Build();
		IContentType site = Document("site").ComposedOf(siteSettings).Build();
		_contentTypeService.Setup(service => service.Get(site.Key)).Returns(site);
		_options.RuleSets["tracking"] = new RuleSetOptions
		{
			ContentTypes = { ["siteSettings"] = new ContentTypeVisibilityOptions { Properties = ["trackingCode"] } },
		};
		_options.Sites["corporate"].Include = ["tracking"];

		HiddenFieldsResponseModel corporate = _service.GetHiddenFields(CorporatePage, site.Key, parentKey: null);
		HiddenFieldsResponseModel campaign = _service.GetHiddenFields(CampaignPage, site.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			// The global siteSettings rule (legacyTab) on both sites, the rule set's trackingCode on corporate only.
			Assert.That(corporate.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(siteSettings, "siteTitle", "trackingCode")));
			Assert.That(campaign.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(siteSettings, "siteTitle")));
			Assert.That(corporate.Warnings, Is.Empty);
		});
	}

	[Test]
	public void An_include_without_a_rule_set_is_skipped_when_the_options_were_not_validated()
	{
		_options.Sites["corporate"].Include = ["missing"];
		_options.RuleSets["nullSet"] = null!;
		_options.Sites["campaign"].Include = ["nullSet"];

		HiddenFieldsResponseModel corporate = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		HiddenFieldsResponseModel campaign = _service.GetHiddenFields(CampaignPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(corporate.PropertyTypeKeys, Does.Contain(PropertyKey(_landingPage, "bannerImage")), "the site's own rules still apply");
			Assert.That(campaign.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "metaKeywords")));
			Assert.That(_logger.At(LogLevel.Error), Is.Empty);
		});
	}

	[Test]
	public void Parent_key_is_passed_to_the_root_resolver()
	{
		var newPage = Guid.NewGuid();
		var parent = Guid.NewGuid();
		_rootNodeResolver
			.Setup(resolver => resolver.Resolve(newPage, parent))
			.Returns(new RootNodeResolution(CorporateRoot, RootResolutionSource.Parent));

		HiddenFieldsResponseModel response = _service.GetHiddenFields(newPage, _landingPage.Key, parent);

		Assert.Multiple(() =>
		{
			Assert.That(response.RootResolution, Is.EqualTo(RootResolutionSource.Parent));
			Assert.That(response.MatchedSite!.Label, Is.EqualTo("corporate"));
			Assert.That(response.PropertyTypeKeys, Does.Contain(PropertyKey(_landingPage, "bannerImage")));
		});
	}

	[Test]
	public void Rule_lookups_ignore_the_case_of_content_type_aliases_even_with_a_case_sensitive_dictionary()
	{
		_options.ContentTypes = new Dictionary<string, ContentTypeVisibilityOptions>
		{
			["LANDINGPAGE"] = new() { Properties = ["title"] },
		};
		_options.Sites["corporate"].ContentTypes = new Dictionary<string, ContentTypeVisibilityOptions>
		{
			["LandingPage"] = new() { Properties = ["bannerImage"] },
		};

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "title", "bannerImage")));
	}

	[Test]
	public void Hide_emptied_containers_option_is_passed_to_the_resolver()
	{
		_options.ContentTypes.Clear();
		_options.Sites["corporate"].ContentTypes["landingPage"] = new() { Properties = ["cssClass", "anchorId"] };

		HiddenFieldsResponseModel on = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		_options.HideEmptiedContainers = false;
		HiddenFieldsResponseModel off = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(on.ContainerKeys, Is.EquivalentTo(ContainerKeys(_landingPage, "settingsTab/advanced", "settingsTab")));
			Assert.That(off.ContainerKeys, Is.Empty);
		});
	}

	[Test]
	public void Unmatched_root_gets_global_rules_a_warning_and_one_log_line_per_root_key()
	{
		_options.Sites.Remove("everythingElse");

		HiddenFieldsResponseModel response = _service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);
		_service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks")), "global rules only");
			Assert.That(response.MatchedSite, Is.Null);
			Assert.That(
				response.Warnings,
				Is.EqualTo(new[] { "PV103: no site rule matches this document's root and no site is the default; only global rules apply." }),
				"the response names neither the root key nor the root name");
			Assert.That(WarningLogs(), Is.Empty, "the configuration analysis logger reports PV103 as a warning, not the request");
			Assert.That(SiteDebugLogs(), Has.Count.EqualTo(1));
			Assert.That(SiteDebugLogs()[0].Message, Does.Contain(OtherRoot.ToString()), "the server log keeps the root key");
		});
		AssertNoRootIdentifiers(response);

		_service.GetHiddenFields(YetAnotherPage, _landingPage.Key, parentKey: null);
		_service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);
		Assert.That(SiteDebugLogs(), Has.Count.EqualTo(2), "a second root key logs once more");

		_onChange!(_options, Options.DefaultName);
		_service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);
		Assert.That(SiteDebugLogs(), Has.Count.EqualTo(3), "a configuration change re-arms the per-root log");
	}

	[Test]
	public void Unmatched_root_with_a_default_site_uses_the_default_without_warning()
	{
		HiddenFieldsResponseModel response = _service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.MatchedSite!.Label, Is.EqualTo("everythingElse"));
			Assert.That(response.MatchedSite.Reason, Is.EqualTo(SiteMatchReason.Default));
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks", "bannerImage")));
			Assert.That(response.Warnings, Is.Empty);
			Assert.That(WarningLogs(), Is.Empty);
		});
	}

	[Test]
	public void New_document_at_the_root_without_a_default_warns_but_does_not_log()
	{
		_options.Sites.Remove("everythingElse");

		HiddenFieldsResponseModel response = _service.GetHiddenFields(NewPageAtRoot, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.RootResolution, Is.EqualTo(RootResolutionSource.None));
			Assert.That(response.MatchedSite, Is.Null);
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks")));
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.RootWithoutSite));
			Assert.That(WarningLogs(), Is.Empty);
		});
	}

	[Test]
	public void Recycle_bin_gets_global_rules_only_and_no_site_warning()
	{
		HiddenFieldsResponseModel response = _service.GetHiddenFields(TrashedPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.RootResolution, Is.EqualTo(RootResolutionSource.RecycleBin));
			Assert.That(response.MatchedSite, Is.Null, "not even the default site applies in the bin");
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks")));
			Assert.That(response.Warnings, Is.Empty);
			Assert.That(WarningLogs(), Is.Empty);
		});
	}

	[Test]
	public void Single_site_install_without_sites_has_no_site_warning()
	{
		_options.Sites.Clear();

		HiddenFieldsResponseModel response = _service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "relatedLinks")));
			Assert.That(response.MatchedSite, Is.Null);
			Assert.That(response.Warnings, Is.Empty);
			Assert.That(WarningLogs(), Is.Empty);
		});
	}

	[Test]
	public void Unknown_aliases_are_reported_as_warnings_with_the_available_containers()
	{
		_options.ContentTypes["landingPage"] = new() { Properties = ["noSuchProperty"], Containers = ["noSuchTab"] };
		_options.Sites.Clear();

		HiddenFieldsResponseModel response = _service.GetHiddenFields(OtherPage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.Warnings, Has.Count.EqualTo(2));
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.UnknownPropertyAlias).And.Contains("'noSuchProperty'"));
			Assert.That(
				response.Warnings,
				Has.One.StartsWith(IssueCodes.UnknownContainerAlias)
					.And.Contains("'noSuchTab'")
					.And.Contains("Available: contentTab, contentTab/main, seoTab, seoTab/meta, settingsTab, settingsTab/advanced."));
		});
	}

	[Test]
	public void Rules_keyed_by_a_composition_apply_to_the_composing_type_global_and_per_site_with_one_content_type_lookup()
	{
		IContentType siteSettings = Document("siteSettings")
			.Tab("legacyTab")
			.Group("legacyTab/general", "siteTitle", "footerText")
			.Group("analytics", "trackingCode")
			.Build();
		IContentType site = Document("site").ComposedOf(siteSettings).Build();
		_contentTypeService.Setup(service => service.Get(site.Key)).Returns(site);
		_options.Sites["corporate"].ContentTypes["siteSettings"] = new ContentTypeVisibilityOptions { Properties = ["trackingCode"] };

		HiddenFieldsResponseModel corporate = _service.GetHiddenFields(CorporatePage, site.Key, parentKey: null);
		HiddenFieldsResponseModel campaign = _service.GetHiddenFields(CampaignPage, site.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			// The global siteSettings rule (legacyTab) on both sites; corporate's own siteSettings rule only there.
			Assert.That(corporate.ContainerKeys, Is.EquivalentTo(ContainerKeys(siteSettings, "legacyTab", "legacyTab/general", "analytics")), "analytics is emptied");
			Assert.That(corporate.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(siteSettings, "siteTitle", "footerText", "trackingCode")));
			Assert.That(campaign.ContainerKeys, Is.EquivalentTo(ContainerKeys(siteSettings, "legacyTab", "legacyTab/general")));
			Assert.That(campaign.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(siteSettings, "siteTitle", "footerText")));
			Assert.That(corporate.Warnings, Is.Empty);
			Assert.That(campaign.Warnings, Is.Empty);
		});
		_contentTypeService.Verify(service => service.Get(site.Key), Times.Exactly(2));
		_contentTypeService.VerifyNoOtherCalls();
	}

	[Test]
	public void An_unknown_alias_in_a_composition_rule_is_a_warning_against_the_composition()
	{
		IContentType siteSettings = Document("siteSettings").Tab("legacyTab").Group("legacyTab/general", "siteTitle").Build();
		IContentType site = Document("site").Tab("contentTab").Group("contentTab/main", "heading").ComposedOf(siteSettings).Build();
		_contentTypeService.Setup(service => service.Get(site.Key)).Returns(site);
		// heading and contentTab exist on site, not on siteSettings.
		_options.ContentTypes["siteSettings"] = new ContentTypeVisibilityOptions { Properties = ["heading"], Containers = ["contentTab"] };
		_options.Sites.Clear();

		HiddenFieldsResponseModel response = _service.GetHiddenFields(OtherPage, site.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.ContainerKeys, Is.Empty);
			Assert.That(
				response.Warnings,
				Is.EqualTo(new[]
				{
					$"{IssueCodes.UnknownPropertyAlias}: property alias 'heading' does not exist on content type 'siteSettings'.",
					$"{IssueCodes.UnknownContainerAlias}: container alias 'contentTab' does not exist on content type 'siteSettings'. Available: legacyTab, legacyTab/general.",
				}));
		});
	}

	[Test]
	public void Name_drift_is_a_warning_without_identifiers_and_is_logged_in_full_once_per_configuration()
	{
		_rootNodeResolver.Setup(resolver => resolver.GetRootName(CorporateRoot)).Returns("Corporate website");

		HiddenFieldsResponseModel response = _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.MatchedSite!.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(response.Warnings, Has.Count.EqualTo(1));
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.RootNodeNameDrift).And.Contains("'corporate'"));
			Assert.That(response.Warnings[0], Does.Not.Contain("Corporate website"));
			Assert.That(WarningLogs(), Is.Empty, "the configuration analysis logger reports PV105 as a warning, not the request");
			Assert.That(SiteDebugLogs(), Has.Count.EqualTo(1), "logged once, not per request");
			Assert.That(
				SiteDebugLogs()[0].Message,
				Does.Contain(IssueCodes.RootNodeNameDrift).And.Contain(CorporateRoot.ToString()).And.Contain("Corporate website").And.Contain("Corporate site"));
		});
		AssertNoRootIdentifiers(response, "Corporate website");

		_onChange!(_options, Options.DefaultName);
		_service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		Assert.That(SiteDebugLogs(), Has.Count.EqualTo(2), "a configuration change re-arms the drift log");
	}

	[Test]
	public void Disposing_the_service_ends_the_options_subscription()
	{
		_service.Dispose();

		_changeSubscription.Verify(subscription => subscription.Dispose(), Times.Once);
	}

	// The multi-site sample of the configuration docs, reduced to what the tests need.
	private static PropertyVisibilityOptions SampleOptions()
	{
		var options = new PropertyVisibilityOptions();
		options.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["relatedLinks"] };
		options.ContentTypes["siteSettings"] = new ContentTypeVisibilityOptions { Containers = ["legacyTab"] };

		var corporate = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" };
		corporate.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions
		{
			Properties = ["bannerImage", "relatedLinks"],
			Containers = ["seoTab", "settingsTab/advanced"],
		};
		corporate.ContentTypes["article"] = new ContentTypeVisibilityOptions { Containers = ["shareTab"] };

		var campaign = new SiteVisibilityOptions { RootNodeName = "Campaign site" };
		campaign.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["metaKeywords"] };

		var everythingElse = new SiteVisibilityOptions { IsDefault = true };
		everythingElse.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["bannerImage"] };

		options.Sites["corporate"] = corporate;
		options.Sites["campaign"] = campaign;
		options.Sites["everythingElse"] = everythingElse;
		return options;
	}

	private IReadOnlyList<LogEntry> WarningLogs() => _logger.At(LogLevel.Warning);

	// Request-time site matching diagnostics (PV103, PV105): Debug, once per root or site per configuration.
	private IReadOnlyList<LogEntry> SiteDebugLogs() => _logger.At(LogLevel.Debug);

	// Repeats of an unexpected per-request failure.
	private List<LogEntry> RepeatedFailureLogs()
		=> _logger.At(LogLevel.Debug).Where(entry => entry.Message.Contains("failed again", StringComparison.Ordinal)).ToList();

	// No root node key or root node name of any configured or resolved root may reach the response.
	private static void AssertNoRootIdentifiers(HiddenFieldsResponseModel response, params string[] extraNames)
	{
		var identifiers = new[] { CorporateRoot, CampaignRoot, OtherRoot, YetAnotherRoot }
			.Select(key => key.ToString())
			.Concat(["Corporate site", "Campaign site", "Some other site", "Yet another site"])
			.Concat(extraNames);

		foreach (var identifier in identifiers)
		{
			Assert.That(response.Warnings, Has.None.Contains(identifier).IgnoreCase, $"a warning contains '{identifier}'");
		}
	}

	private void Resolves(Guid documentKey, Guid rootKey)
		=> _rootNodeResolver
			.Setup(resolver => resolver.Resolve(documentKey, It.IsAny<Guid?>()))
			.Returns(new RootNodeResolution(rootKey, RootResolutionSource.Document));
}
