using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.HealthChecks;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.HealthChecks;

[TestFixture]
public sealed class PropertyVisibilityHealthCheckTests
{
	private static readonly Guid CorporateRoot = ConfigurationAnalyzerTestSite.CorporateRoot;

	private Mock<IConfigurationAnalyzer> _analyzer = null!;
	private ListLogger<PropertyVisibilityHealthCheck> _logger = null!;
	private ConfigurationAnalysisSummary _summary = null!;

	[SetUp]
	public void SetUp()
	{
		_analyzer = new Mock<IConfigurationAnalyzer>();
		_logger = new ListLogger<PropertyVisibilityHealthCheck>();
		_summary = new ConfigurationAnalysisSummary(
			OptionsValid: true,
			Enabled: true,
			ActiveSource: ConfigurationSource.Appsettings,
			FilePath: ConfigurationAnalyzerTestSite.FilePath,
			LoadedAt: ConfigurationAnalyzerTestSite.LoadedAt,
			RulesHash: ConfigurationAnalyzerTestSite.SampleRulesHash,
			SiteCount: 3,
			RootNodeKeys: [CorporateRoot, ConfigurationAnalyzerTestSite.CampaignRoot],
			UmbracoVersion: new Version(17, 6, 2),
			TestedUmbracoVersions: [new Version(17, 6, 2)]);
	}

	[Test]
	public void The_attribute_carries_the_metadata_and_Umbraco_can_discover_the_check()
	{
		HealthCheckAttribute? attribute = typeof(PropertyVisibilityHealthCheck).GetCustomAttribute<HealthCheckAttribute>(inherit: false);
		PropertyVisibilityHealthCheck check = Create();

		Assert.Multiple(() =>
		{
			Assert.That(attribute, Is.Not.Null);
			Assert.That(check.Id, Is.EqualTo(Guid.Parse("b35b2a1d-1954-42f2-a2ec-73f7d2f366b4")));
			Assert.That(check.Name, Is.EqualTo("Property Visibility configuration"));
			Assert.That(check.Group, Is.EqualTo("Configuration"));
			Assert.That(check.Description, Is.Not.Empty);

			// TypeLoader.GetTypes<HealthCheck>() filters the discovered IDiscoverable types: public, concrete, assignable.
			Assert.That(typeof(IDiscoverable).IsAssignableFrom(typeof(PropertyVisibilityHealthCheck)), Is.True);
			Assert.That(typeof(PropertyVisibilityHealthCheck).IsPublic, Is.True);
			Assert.That(typeof(PropertyVisibilityHealthCheck).IsAbstract, Is.False);
		});
	}

	[TestCase(IssueSeverity.Error, StatusResultType.Error)]
	[TestCase(IssueSeverity.Warning, StatusResultType.Warning)]
	[TestCase(IssueSeverity.Info, StatusResultType.Info)]
	public void Severities_map_to_result_types(IssueSeverity severity, StatusResultType expected)
		=> Assert.That(PropertyVisibilityHealthCheck.ToResultType(severity), Is.EqualTo(expected));

	[Test]
	public async Task A_clean_configuration_is_one_success_status_with_the_summary()
	{
		Returns();

		List<HealthCheckStatus> statuses = (await Create().GetStatusAsync()).ToList();

		Assert.That(statuses, Has.Count.EqualTo(1));
		HealthCheckStatus summary = statuses[0];
		Assert.Multiple(() =>
		{
			Assert.That(summary.ResultType, Is.EqualTo(StatusResultType.Success));
			Assert.That(summary.Message, Does.Contain("configuration is valid: every configured root node, content type, property and container exists"));
			Assert.That(summary.Message, Does.Contain($"Rules source: appsettings (the rules file {ConfigurationAnalyzerTestSite.FilePath} does not exist or holds no JSON value)."));
			Assert.That(summary.Message, Does.Contain($"Loaded at 2026-09-25 14:48:35 UTC; rules hash <code>{ConfigurationAnalyzerTestSite.SampleRulesHash}</code>."));
			Assert.That(summary.Message, Does.Contain("3 sites configured, 2 root nodes in the content tree."));
			Assert.That(summary.Message, Does.Contain("Umbraco 17.6.2 (tested: 17.6.2)."));
			Assert.That(summary.ReadMoreLink, Is.Null);
		});
	}

	[Test]
	public void One_status_per_issue_after_the_summary_most_severe_first()
	{
		Returns(
			new ConfigurationAnalysisIssue(IssueCodes.Disabled, IssueSeverity.Info, "The package is disabled.", Path: "PropertyVisibility:Enabled"),
			new ConfigurationAnalysisIssue(IssueCodes.UnknownPropertyAlias, IssueSeverity.Warning, "Property 'bannerImg' does not exist.", "corporate", "landingPage", "bannerImage", "Sites:corporate:ContentTypes:landingPage:Properties"),
			new ConfigurationAnalysisIssue(IssueCodes.ConfigurationInvalid, IssueSeverity.Error, "The configuration cannot be read.", Path: "PropertyVisibility"),
			new ConfigurationAnalysisIssue(IssueCodes.UnknownContainerAlias, IssueSeverity.Warning, "Container 'seoTabb' does not exist.", "corporate", "landingPage", "seoTab"));

		IReadOnlyList<HealthCheckStatus> statuses = Create().GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(
				statuses.Select(status => status.ResultType),
				Is.EqualTo(new[] { StatusResultType.Info, StatusResultType.Error, StatusResultType.Warning, StatusResultType.Warning, StatusResultType.Info }));
			Assert.That(statuses[0].Message, Does.StartWith("The Property Visibility configuration has 1 error and 2 warnings; see below."));
			Assert.That(statuses[1].Message, Does.StartWith("<strong>PV008</strong> <code>PropertyVisibility</code>: The configuration cannot be read."));
			Assert.That(statuses[2].Message, Is.EqualTo(
				"<strong>PV201</strong> <code>Sites:corporate:ContentTypes:landingPage:Properties</code>: Property &#39;bannerImg&#39; does not exist. Did you mean <code>bannerImage</code>?"));
			Assert.That(statuses[3].Message, Is.EqualTo("<strong>PV202</strong> Container &#39;seoTabb&#39; does not exist. Did you mean <code>seoTab</code>?"));
			Assert.That(statuses[4].Message, Does.StartWith("<strong>PV302</strong>"));
			Assert.That(statuses.Skip(1).Select(status => status.ReadMoreLink), Is.All.EqualTo(PropertyVisibilityHealthCheck.IssueCodesDocumentationUrl));
		});
	}

	[Test]
	public void Only_informational_issues_keep_the_summary_a_success()
	{
		Returns(new ConfigurationAnalysisIssue(IssueCodes.UntestedUmbracoVersion, IssueSeverity.Info, "Umbraco 17.7.0 is not in the tested list.", Path: "Umbraco"));

		IReadOnlyList<HealthCheckStatus> statuses = Create().GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Select(status => status.ResultType), Is.EqualTo(new[] { StatusResultType.Success, StatusResultType.Info }));
			Assert.That(statuses[0].Message, Does.StartWith("The Property Visibility configuration is valid"));
		});
	}

	[Test]
	public void Every_value_in_a_message_is_html_encoded()
	{
		_summary = _summary with { ActiveSource = ConfigurationSource.File, FilePath = "/site/<b>rules</b>.json" };
		Returns(new ConfigurationAnalysisIssue(
			IssueCodes.RootWithoutSite,
			IssueSeverity.Warning,
			"The root node '<img src=x onerror=alert(1)>' matches no site.",
			Suggestion: "<script>alert(2)</script>",
			Path: "Sites"));

		IReadOnlyList<HealthCheckStatus> statuses = Create().GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(statuses[1].Message, Does.Contain("&lt;img src=x onerror=alert(1)&gt;"));
			Assert.That(statuses[1].Message, Does.Contain("&lt;script&gt;alert(2)&lt;/script&gt;"));
			Assert.That(statuses[0].Message, Does.Contain("Rules source: the rules file /site/&lt;b&gt;rules&lt;/b&gt;.json."));
			Assert.That(statuses.Select(status => status.Message), Has.None.Contains("<img").And.None.Contains("<script").And.None.Contains("<b>"));
		});
	}

	[TestCase(ConfigurationSource.File, ConfigurationAnalyzerTestSite.FilePath, "Rules source: the rules file /site/PropertyVisibility.config.json.")]
	[TestCase(ConfigurationSource.Appsettings, null, "Rules source: appsettings (the rules file is turned off).")]
	[TestCase(ConfigurationSource.None, ConfigurationAnalyzerTestSite.FilePath, "Rules source: none. The rules file /site/PropertyVisibility.config.json has never loaded")]
	public void The_summary_names_the_rules_source(ConfigurationSource source, string? filePath, string expected)
	{
		_summary = _summary with { ActiveSource = source, FilePath = filePath };
		Returns();

		Assert.That(Create().GetStatuses()[0].Message, Does.Contain(expected));
	}

	[Test]
	public void The_summary_before_the_first_load_says_so()
	{
		_summary = _summary with { ActiveSource = ConfigurationSource.None, FilePath = null, LoadedAt = null, RulesHash = null };
		Returns();

		string message = Create().GetStatuses()[0].Message;

		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("Rules source: not loaded yet."));
			Assert.That(message, Does.Not.Contain("Loaded at"));
		});
	}

	[Test]
	public void The_summary_of_invalid_options_describes_the_last_successful_load()
	{
		_summary = _summary with { OptionsValid = false, Enabled = null, SiteCount = 0 };
		Returns(new ConfigurationAnalysisIssue(IssueCodes.ConfigurationInvalid, IssueSeverity.Error, "The configuration cannot be read.", Path: "PropertyVisibility"));

		string message = Create().GetStatuses()[0].Message;

		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("Last successful load at 2026-09-25 14:48:35 UTC"));
			Assert.That(message, Does.Not.Contain("sites configured"));
		});
	}

	[Test]
	public void The_summary_of_invalid_options_without_any_successful_load_says_so()
	{
		_summary = _summary with { OptionsValid = false, Enabled = null, SiteCount = 0, LoadedAt = null, RulesHash = null };
		Returns(new ConfigurationAnalysisIssue(IssueCodes.ConfigurationInvalid, IssueSeverity.Error, "The configuration fails validation.", Path: "PropertyVisibility"));

		string message = Create().GetStatuses()[0].Message;

		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("No configuration has loaded successfully since the site started."));
			Assert.That(message, Does.Not.Contain("rules hash"));
		});
	}

	[Test]
	public void The_summary_flags_an_untested_Umbraco_version_and_a_disabled_package()
	{
		_summary = _summary with { UmbracoVersion = new Version(17, 8, 0), Enabled = false, SiteCount = 1, RootNodeKeys = [CorporateRoot] };
		Returns();

		string message = Create().GetStatuses()[0].Message;

		Assert.Multiple(() =>
		{
			Assert.That(message, Does.Contain("Umbraco 17.8.0 is not in the tested list (tested: 17.6.2)."));
			Assert.That(message, Does.Contain("1 site configured, 1 root node in the content tree. The package is disabled."));
		});
	}

	[Test]
	public void A_failing_analysis_is_one_error_status_and_is_logged()
	{
		_analyzer.Setup(analyzer => analyzer.Analyze()).Throws(new InvalidOperationException("database <down>"));

		IReadOnlyList<HealthCheckStatus> statuses = Create().GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(statuses, Has.Count.EqualTo(1));
			Assert.That(statuses[0].ResultType, Is.EqualTo(StatusResultType.Error));
			Assert.That(statuses[0].Message, Does.Contain("database &lt;down&gt;"));
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void With_the_real_analyzer_a_wrong_alias_a_wrong_root_key_and_a_hidden_mandatory_property_are_listed()
	{
		var site = new ConfigurationAnalyzerTestSite();
		site.Options.Sites["corporate"].RootNodeKey = ConfigurationAnalyzerTestSite.CorporateSection;
		site.Options.Sites["corporate"].ContentTypes["landingPage"].Properties.Add("bannerImg");
		ConfigurationAnalyzerTestSite.Mandatory(site.LandingPage, "relatedLinks");
		var check = new PropertyVisibilityHealthCheck(site.CreateAnalyzer(), _logger);

		IReadOnlyList<HealthCheckStatus> statuses = check.GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(statuses[0].ResultType, Is.EqualTo(StatusResultType.Info));
			Assert.That(statuses[0].Message, Does.StartWith("The Property Visibility configuration has 0 errors and 3 warnings; see below."));
			Assert.That(
				statuses.Skip(1).Select(status => status.ResultType),
				Is.EqualTo(new[] { StatusResultType.Warning, StatusResultType.Warning, StatusResultType.Warning, StatusResultType.Info }),
				"the warnings, then the sample's PV205 notice");
			Assert.That(statuses.Skip(1).Select(status => status.Message), Has.One.StartWith("<strong>PV101</strong>"));
			Assert.That(statuses.Skip(1).Select(status => status.Message), Has.One.StartWith("<strong>PV201</strong>").And.Contains("Did you mean <code>bannerImage</code>?"));
			Assert.That(statuses.Skip(1).Select(status => status.Message), Has.One.StartWith("<strong>PV203</strong>"));
		});
	}

	[Test]
	public void With_the_real_analyzer_the_sample_is_a_success_with_the_composition_notice()
	{
		var check = new PropertyVisibilityHealthCheck(new ConfigurationAnalyzerTestSite().CreateAnalyzer(), _logger);

		IReadOnlyList<HealthCheckStatus> statuses = check.GetStatuses();

		Assert.Multiple(() =>
		{
			Assert.That(statuses.Select(status => status.ResultType), Is.EqualTo(new[] { StatusResultType.Success, StatusResultType.Info }));
			Assert.That(
				statuses[1].Message,
				Does.StartWith("<strong>PV205</strong> <code>ContentTypes:siteSettings</code>: Content type &#39;siteSettings&#39; is a composition"));
		});
	}

	private void Returns(params ConfigurationAnalysisIssue[] issues)
		=> _analyzer.Setup(analyzer => analyzer.Analyze()).Returns(() => new ConfigurationAnalysis(_summary, issues));

	private PropertyVisibilityHealthCheck Create() => new(_analyzer.Object, _logger);
}
