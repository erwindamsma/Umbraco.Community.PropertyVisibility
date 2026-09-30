using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.Services.ConfigurationAnalyzerTestSite;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     <see cref="ConfigurationAnalyzer" /> over <see cref="ConfigurationAnalyzerTestSite" />: one test (or more) per issue
///     code the analyzer produces, suggestions, compositions, and the clean sample.
/// </summary>
[TestFixture]
public sealed class ConfigurationAnalyzerTests
{
	private ConfigurationAnalyzerTestSite _site = null!;

	[SetUp]
	public void SetUp() => _site = new ConfigurationAnalyzerTestSite();

	[Test]
	public void The_sample_has_only_the_composition_notice_and_a_complete_summary()
	{
		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.CompositionRuleReach }), "siteSettings is a composition of landingPage");
			Assert.That(analysis.IsHealthy, Is.True);
			Assert.That(analysis.Summary.OptionsValid, Is.True);
			Assert.That(analysis.Summary.Enabled, Is.True);
			Assert.That(analysis.Summary.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(analysis.Summary.FilePath, Is.EqualTo(FilePath));
			Assert.That(analysis.Summary.LoadedAt, Is.EqualTo(LoadedAt));
			Assert.That(analysis.Summary.RulesHash, Is.EqualTo(SampleRulesHash));
			Assert.That(analysis.Summary.SiteCount, Is.EqualTo(3));
			Assert.That(analysis.Summary.RootNodeKeys, Is.EqualTo(new[] { CorporateRoot, CampaignRoot }));
			Assert.That(analysis.Summary.UmbracoVersion, Is.EqualTo(new Version(17, 6, 2)));
			Assert.That(analysis.Summary.TestedUmbracoVersions, Is.EqualTo(Constants.TestedUmbracoVersions));
			Assert.That(analysis.Summary.IsTestedUmbracoVersion, Is.True);
		});
	}

	[Test]
	public void The_options_are_read_before_the_configuration_info()
	{
		Analyze();

		Assert.That(_site.Reads, Is.EqualTo(new[] { "options", "configurationInfo" }));
	}

	[Test]
	public void Load_issues_of_the_rules_file_are_reported_with_their_path_and_suggestion()
	{
		_site.State = _site.State with
		{
			ActiveSource = ConfigurationSource.File,
			Issues =
			[
				new ConfigurationIssue(IssueCodes.InvalidJson, IssueSeverity.Error, "Rules file 'PropertyVisibility.config.json' is not valid JSON: line 3, position 49.", "$"),
				new ConfigurationIssue(IssueCodes.UnknownKey, IssueSeverity.Error, "Unknown key 'RootNodeNme'.", "$.Sites.corporate.RootNodeNme", "RootNodeName"),
				new ConfigurationIssue(IssueCodes.BothSourcesDefineRules, IssueSeverity.Warning, "Both appsettings and the rules file define rules; the file wins.", "PropertyVisibility"),
			],
		};

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			ConfigurationAnalysisIssue invalidJson = Single(analysis, IssueCodes.InvalidJson);
			Assert.That(invalidJson.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(invalidJson.Path, Is.EqualTo("$"));
			Assert.That(invalidJson.Message, Does.Contain("line 3, position 49"));

			ConfigurationAnalysisIssue unknownKey = Single(analysis, IssueCodes.UnknownKey);
			Assert.That(unknownKey.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(unknownKey.Path, Is.EqualTo("$.Sites.corporate.RootNodeNme"));
			Assert.That(unknownKey.Suggestion, Is.EqualTo("RootNodeName"));
			Assert.That(unknownKey.SiteLabel, Is.Null, "a JSON path is not split into a site label");
			Assert.That(unknownKey.ToString(), Does.EndWith("Did you mean 'RootNodeName'?"));

			ConfigurationAnalysisIssue bothSources = Single(analysis, IssueCodes.BothSourcesDefineRules);
			Assert.That(bothSources.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(analysis.Summary.ActiveSource, Is.EqualTo(ConfigurationSource.File));
		});
	}

	[Test]
	public void An_unknown_appsettings_key_is_PV008_with_the_binder_message()
	{
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings("""
			{ "Sites": { "corporate": { "RootNodeNme": "Corporate site", "IsDefault": true } } }
			"""));

		ConfigurationAnalysis analysis = _site.CreateAnalyzer(configuration.Monitor).Analyze();

		Assert.Multiple(() =>
		{
			ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.ConfigurationInvalid);
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(issue.Message, Does.Contain("nothing is hidden"));
			Assert.That(issue.Message, Does.Contain("RootNodeNme"), "the binder's message names the unknown key");
			Assert.That(analysis.Issues.Select(i => i.Code), Is.EqualTo(new[] { IssueCodes.ConfigurationInvalid }));
			Assert.That(analysis.Summary.OptionsValid, Is.False);
			Assert.That(analysis.Summary.Enabled, Is.Null);
			Assert.That(analysis.Summary.SiteCount, Is.Zero);
			Assert.That(analysis.IsHealthy, Is.False);
		});
		_site.ContentTypeService.Verify(service => service.GetAll(), Times.Never);
	}

	[Test]
	public void A_value_of_the_wrong_type_is_PV008_with_the_binder_message()
	{
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings("""
			{ "Sites": { "corporate": { "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7", "IsDefault": true } } }
			"""));

		ConfigurationAnalysis analysis = _site.CreateAnalyzer(configuration.Monitor).Analyze();

		Assert.That(Single(analysis, IssueCodes.ConfigurationInvalid).Message, Does.Contain("RootNodeKey"));
	}

	[Test]
	public void A_validation_failure_from_the_real_options_pipeline_is_PV008_plus_the_validator_issue()
	{
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings("""
			{ "Sites": { "noIdentity": { "ContentTypes": { "landingPage": { "Properties": ["title"] } } } } }
			"""));

		ConfigurationAnalysis analysis = _site.CreateAnalyzer(configuration.Monitor).Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.ConfigurationInvalid, IssueCodes.SiteWithoutIdentity }));
			ConfigurationAnalysisIssue detail = Single(analysis, IssueCodes.SiteWithoutIdentity);
			Assert.That(detail.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(detail.Path, Is.EqualTo("Sites:noIdentity"));
			Assert.That(detail.SiteLabel, Is.EqualTo("noIdentity"));
			Assert.That(detail.Message, Does.StartWith("Site 'noIdentity' has none of RootNodeKey"));
			Assert.That(analysis.Summary.OptionsValid, Is.False);
		});
	}

	[Test]
	public void Every_validator_code_is_reported_as_its_own_issue()
	{
		var invalid = new PropertyVisibilityOptions
		{
			ContentTypes = { ["landingPage"] = Block(containers: ["contentTab/"]) },
			Sites =
			{
				["noIdentity"] = new SiteVisibilityOptions(),
				["first"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, IsDefault = true },
				["second"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, IsDefault = true },
				["bad:label"] = new SiteVisibilityOptions { RootNodeName = "Campaign site" },
			},
		};
		ValidateOptionsResult result = new PropertyVisibilityOptionsValidator().Validate(Microsoft.Extensions.Options.Options.DefaultName, invalid);
		_site.OptionsFailure = new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(PropertyVisibilityOptions), result.Failures);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(
				analysis.Issues.Select(issue => issue.Code),
				Is.EquivalentTo(new[]
				{
					IssueCodes.ConfigurationInvalid,
					IssueCodes.SiteWithoutIdentity,
					IssueCodes.DuplicateSiteIdentity,
					IssueCodes.MultipleDefaultSites,
					IssueCodes.InvalidContainerAlias,
					IssueCodes.InvalidSiteLabel,
				}));
			Assert.That(analysis.Issues.All(issue => issue.Severity == IssueSeverity.Error), Is.True);
			Assert.That(Single(analysis, IssueCodes.ConfigurationInvalid).Message, Does.Contain("5 validation errors are listed separately"));
			Assert.That(Single(analysis, IssueCodes.SiteWithoutIdentity).SiteLabel, Is.EqualTo("noIdentity"));
			Assert.That(Single(analysis, IssueCodes.DuplicateSiteIdentity).SiteLabel, Is.EqualTo("second"));
			Assert.That(Single(analysis, IssueCodes.MultipleDefaultSites).Path, Is.EqualTo("Sites"));
			Assert.That(Single(analysis, IssueCodes.InvalidContainerAlias).ContentTypeAlias, Is.EqualTo("landingPage"));
			Assert.That(Single(analysis, IssueCodes.InvalidContainerAlias).Path, Is.EqualTo("ContentTypes:landingPage:Containers"));
			Assert.That(Single(analysis, IssueCodes.InvalidSiteLabel).Message, Does.Contain("'bad:label'"));
			Assert.That(Single(analysis, IssueCodes.InvalidSiteLabel).SiteLabel, Is.Null, "a label with a colon cannot be read back from its path");
		});
	}

	[Test]
	public void A_validation_failure_the_analyzer_cannot_parse_is_kept_in_the_PV008_message()
	{
		_site.OptionsFailure = new OptionsValidationException(
			Microsoft.Extensions.Options.Options.DefaultName,
			typeof(PropertyVisibilityOptions),
			["DataAnnotation validation failed for 'PropertyVisibilityOptions' members: 'ConfigFile' with the error: 'too long'."]);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues, Has.Count.EqualTo(1));
			Assert.That(Single(analysis, IssueCodes.ConfigurationInvalid).Message, Does.Contain("DataAnnotation validation failed"));
		});
	}

	[Test]
	public void A_RootNodeKey_below_a_root_is_PV101_suggesting_the_root_key_and_listing_the_roots()
	{
		_site.Options.Sites["corporate"].RootNodeKey = CorporateSection;
		_site.Options.Sites["corporate"].RootNodeName = null;

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeKeyNotARoot);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:RootNodeKey"));
			Assert.That(issue.Message, Does.Contain($"RootNodeKey {CorporateSection} of site 'corporate' is not a root node"));
			Assert.That(issue.Message, Does.Contain($"it is a document under the root node 'Corporate site' ({CorporateRoot})"));
			Assert.That(issue.Message, Does.Contain("The site never matches by key."));
			Assert.That(issue.Message, Does.Contain($"Root nodes: 'Corporate site' ({CorporateRoot}), 'Campaign site' ({CampaignRoot})."));
			Assert.That(issue.Suggestion, Is.EqualTo(CorporateRoot.ToString("D")));
			Assert.That(SiteIssueCodes(analysis), Is.EqualTo(new[] { IssueCodes.RootNodeKeyNotARoot }), "the default site still catches the root");
		});
	}

	[Test]
	public void An_unknown_RootNodeKey_is_PV101_and_notes_the_name_that_still_matches()
	{
		_site.Options.Sites["corporate"].RootNodeKey = UnknownKey;

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeKeyNotARoot);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Message, Does.Contain("no document with this key exists"));
			Assert.That(issue.Message, Does.Contain($"The site still matches the root 'Corporate site' ({CorporateRoot}) by RootNodeName."));
			Assert.That(issue.Suggestion, Is.EqualTo(CorporateRoot.ToString("D")));
			Assert.That(analysis.Issues.Select(i => i.Code), Has.None.EqualTo(IssueCodes.RootNodeNameNotFound));
		});
	}

	[Test]
	public void A_RootNodeKey_in_the_recycle_bin_is_PV101()
	{
		_site.Options.Sites["corporate"].RootNodeKey = TrashedPage;
		_site.Options.Sites["corporate"].RootNodeName = null;
		_site.Options.Sites["corporate"].IsDefault = true;
		_site.Options.Sites["everythingElse"].IsDefault = false;
		_site.Options.Sites["everythingElse"].RootNodeName = "Campaign site";
		_site.Options.Sites.Remove("campaign");

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeKeyNotARoot);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Message, Does.Contain("it is a document in the recycle bin"));
			Assert.That(issue.Message, Does.Contain("The site still applies as the default site."));
			Assert.That(issue.Suggestion, Is.Null);
		});
	}

	[Test]
	public void A_RootNodeName_matching_no_root_is_PV102_suggesting_the_closest_root_name()
	{
		_site.Options.Sites["campaign"].RootNodeName = "Campain site";

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeNameNotFound);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("campaign"));
			Assert.That(issue.Path, Is.EqualTo("Sites:campaign:RootNodeName"));
			Assert.That(issue.Message, Does.Contain("RootNodeName 'Campain site' of site 'campaign' matches no root node."));
			Assert.That(issue.Message, Does.Contain("The site's rules never apply to any root."));
			Assert.That(issue.Message, Does.Contain($"'Campaign site' ({CampaignRoot})"));
			Assert.That(issue.Suggestion, Is.EqualTo("Campaign site"));
		});
	}

	[Test]
	public void A_RootNodeName_far_from_every_root_name_gets_no_suggestion()
	{
		_site.Options.Sites["campaign"].RootNodeName = "Intranet";

		ConfigurationAnalysis analysis = Analyze();

		Assert.That(Single(analysis, IssueCodes.RootNodeNameNotFound).Suggestion, Is.Null);
	}

	[Test]
	public void A_root_without_a_matching_site_and_no_default_is_PV103()
	{
		_site.Roots((CorporateRoot, "Corporate site"), (CampaignRoot, "Campaign site"), (OtherRoot, "Other site"));
		_site.Options.Sites.Remove("everythingElse");

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootWithoutSite);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.Message, Does.Contain($"The root node 'Other site' ({OtherRoot}) matches no site and no site is the default"));
			Assert.That(issue.Path, Is.EqualTo("Sites"));
			Assert.That(issue.SiteLabel, Is.Null);
		});
	}

	[Test]
	public void PV103_suggests_a_site_without_rules_for_a_root_that_is_not_a_site()
	{
		_site.Roots((CorporateRoot, "Corporate site"), (CampaignRoot, "Campaign site"), (OtherRoot, "Settings"));
		_site.Options.Sites.Remove("everythingElse");

		ConfigurationAnalysisIssue issue = Single(Analyze(), IssueCodes.RootWithoutSite);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Message, Does.Contain("Add a site with this RootNodeKey; for a root that is not a site, such as a settings or shared content root, give that site no rules."));
			Assert.That(issue.Message, Does.EndWith("A site marked IsDefault also ends this warning, but for every root no other site matches, roots added later included."));
		});
	}

	[Test]
	public void A_site_with_only_a_RootNodeKey_ends_PV103_for_its_root_and_a_root_added_later_is_still_PV103()
	{
		var newRoot = Guid.Parse("7d6c5b4a-3f2e-4d1c-8b0a-9f8e7d6c5b4a");
		_site.Roots((CorporateRoot, "Corporate site"), (CampaignRoot, "Campaign site"), (OtherRoot, "Settings"));
		_site.Options.Sites.Remove("everythingElse");
		_site.Options.Sites["settings"] = new SiteVisibilityOptions { RootNodeKey = OtherRoot };

		ConfigurationAnalysis withSettingsSite = Analyze();
		_site.Roots((CorporateRoot, "Corporate site"), (CampaignRoot, "Campaign site"), (OtherRoot, "Settings"), (newRoot, "New site"));
		ConfigurationAnalysis withNewRoot = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(SiteIssueCodes(withSettingsSite), Is.Empty, "a site without rules is not an issue");
			Assert.That(Single(withNewRoot, IssueCodes.RootWithoutSite).Message, Does.Contain($"The root node 'New site' ({newRoot}) matches no site"));
		});
	}

	[Test]
	public void Without_sites_no_root_is_PV103()
	{
		_site.Options.Sites.Clear();
		_site.Options.ContentTypes["landingPage"] = Block(["bannerImage"]);

		ConfigurationAnalysis analysis = Analyze();

		Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.CompositionRuleReach }));
	}

	[Test]
	public void An_unknown_content_type_alias_is_PV104_suggesting_the_closest_alias()
	{
		ContentTypeVisibilityOptions block = _site.Options.Sites["corporate"].ContentTypes["landingPage"];
		_site.Options.Sites["corporate"].ContentTypes.Remove("landingPage");
		_site.Options.Sites["corporate"].ContentTypes["landingPge"] = block;

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnknownContentTypeAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(issue.ContentTypeAlias, Is.EqualTo("landingPge"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:ContentTypes:landingPge"));
			Assert.That(issue.Message, Does.Contain("Content type 'landingPge' in site 'corporate' does not exist"));
			Assert.That(issue.Suggestion, Is.EqualTo("landingPage"));
			Assert.That(
				analysis.Issues.Select(i => i.Code),
				Is.EquivalentTo(new[] { IssueCodes.UnknownContentTypeAlias, IssueCodes.CompositionRuleReach }),
				"the unknown type's aliases are not checked");
		});
	}

	[Test]
	public void Content_type_aliases_match_case_insensitively()
	{
		ContentTypeVisibilityOptions block = _site.Options.ContentTypes["siteSettings"];
		_site.Options.ContentTypes.Clear();
		_site.Options.ContentTypes["SITESETTINGS"] = block;

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.CompositionRuleReach }));
			Assert.That(analysis.Issues[0].ContentTypeAlias, Is.EqualTo("SITESETTINGS"), "as configured");
			Assert.That(analysis.Issues[0].Message, Does.StartWith("Content type 'siteSettings' is a composition"));
		});
	}

	[Test]
	public void A_RootNodeName_that_drifted_from_the_root_matched_by_key_is_PV105_suggesting_the_current_name()
	{
		_site.Options.Sites["corporate"].RootNodeName = "Old corporate name";

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeNameDrift);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:RootNodeName"));
			Assert.That(issue.Message, Does.Contain(CorporateRoot.ToString()), "the health check is admin-facing and names the root");
			Assert.That(issue.Message, Does.Contain("'Old corporate name'"));
			Assert.That(issue.Message, Does.Contain("'Corporate site'"));
			Assert.That(issue.Suggestion, Is.EqualTo("Corporate site"));
			Assert.That(analysis.Issues.Select(i => i.Code), Has.None.EqualTo(IssueCodes.RootNodeNameNotFound), "a site matched by key uses its name only for drift");
		});
	}

	[Test]
	public void A_stale_RootNodeName_equal_to_another_roots_name_does_not_pull_that_root_into_the_site()
	{
		// The roots were renamed: corporate still carries the old name, which is now the other root's name.
		_site.Options.Sites.Remove("campaign");
		_site.Options.Sites["corporate"].RootNodeName = "Campaign site";

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue drift = Single(analysis, IssueCodes.RootNodeNameDrift);
		Assert.Multiple(() =>
		{
			Assert.That(drift.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(drift.Message, Does.Contain($"'Campaign site' is the name of the root node 'Campaign site' ({CampaignRoot}), which this site does not match"));
			Assert.That(SiteIssueCodes(analysis), Is.EqualTo(new[] { IssueCodes.RootNodeNameDrift }), "the campaign root falls to the default site");
		});
	}

	[Test]
	public void A_stale_RootNodeName_equal_to_another_roots_name_leaves_that_root_without_a_site_when_there_is_no_default()
	{
		_site.Options.Sites.Remove("campaign");
		_site.Options.Sites.Remove("everythingElse");
		_site.Options.Sites["corporate"].RootNodeName = "Campaign site";

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(SiteIssueCodes(analysis), Is.EquivalentTo(new[] { IssueCodes.RootNodeNameDrift, IssueCodes.RootWithoutSite }));
			Assert.That(Single(analysis, IssueCodes.RootWithoutSite).Message, Does.Contain($"'Campaign site' ({CampaignRoot}) matches no site"));
		});
	}

	[Test]
	public void A_name_only_site_whose_root_another_site_holds_by_key_is_PV102()
	{
		// Migrated from name to key, and the old entry was left behind.
		_site.Options.Sites["campaignNew"] = new SiteVisibilityOptions { RootNodeKey = CampaignRoot, ContentTypes = { ["landingPage"] = Block(["metaKeywords"]) } };

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeNameNotFound);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("campaign"));
			Assert.That(issue.Path, Is.EqualTo("Sites:campaign:RootNodeName"));
			Assert.That(issue.Message, Does.Contain($"matches the root node 'Campaign site' ({CampaignRoot}), but site 'campaignNew' claims that root by RootNodeKey"));
			Assert.That(issue.Message, Does.Contain("The site's rules never apply to any root."));
			Assert.That(issue.Suggestion, Is.Null);
			Assert.That(SiteIssueCodes(analysis), Is.EqualTo(new[] { IssueCodes.RootNodeNameNotFound }));
		});
	}

	[Test]
	public void An_invalid_RootNodeKey_whose_name_root_another_site_holds_is_PV101_without_the_still_matches_claim()
	{
		_site.Options.Sites["campaign"].RootNodeKey = UnknownKey;
		_site.Options.Sites["campaignNew"] = new SiteVisibilityOptions { RootNodeKey = CampaignRoot };

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.RootNodeKeyNotARoot);
		Assert.Multiple(() =>
		{
			Assert.That(issue.SiteLabel, Is.EqualTo("campaign"));
			Assert.That(issue.Message, Does.Not.Contain("still matches"));
			Assert.That(issue.Message, Does.Contain($"Its RootNodeName matches the root 'Campaign site' ({CampaignRoot}), but site 'campaignNew' claims that root by RootNodeKey."));
			Assert.That(issue.Message, Does.Contain("The site never applies to any root."));
			Assert.That(issue.Suggestion, Is.Null, "suggesting a key another site holds would only make a duplicate");
			Assert.That(SiteIssueCodes(analysis), Is.EqualTo(new[] { IssueCodes.RootNodeKeyNotARoot }), "PV101 already covers the name");
		});
	}

	[Test]
	public void An_unknown_property_alias_is_PV201_suggesting_the_closest_and_composed_properties_are_known()
	{
		_site.Options.Sites["corporate"].ContentTypes["landingPage"] = Block(["bannerImg", "legacyNotes"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnknownPropertyAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(issue.ContentTypeAlias, Is.EqualTo("landingPage"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:ContentTypes:landingPage:Properties"));
			Assert.That(issue.Message, Does.Contain("Property 'bannerImg' in site 'corporate' does not exist on content type 'landingPage'"));
			Assert.That(issue.Suggestion, Is.EqualTo("bannerImage"));
		});
	}

	[Test]
	public void An_unknown_container_alias_is_PV202_listing_tab_and_group_forms_including_compositions()
	{
		_site.Options.Sites["corporate"].ContentTypes["landingPage"] = Block(containers: ["seoTabb", "legacyTab", "legacyTab/legacy"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnknownContainerAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.SiteLabel, Is.EqualTo("corporate"));
			Assert.That(issue.ContentTypeAlias, Is.EqualTo("landingPage"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:ContentTypes:landingPage:Containers"));
			Assert.That(issue.Message, Does.Contain("Container 'seoTabb' in site 'corporate' does not exist on content type 'landingPage'"));
			Assert.That(
				issue.Message,
				Does.Contain("Available containers: contentTab, contentTab/main, legacyTab, legacyTab/legacy, seoTab, seoTab/meta, settingsTab, settingsTab/advanced."));
			Assert.That(issue.Suggestion, Is.EqualTo("seoTab"));
		});
	}

	[Test]
	public void A_container_alias_on_a_type_without_containers_says_so()
	{
		_site.ContentTypes.Add(TestContentType.Element("plainElement").Build());
		_site.Options.ContentTypes["plainElement"] = Block(containers: ["seoTab"]);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(Single(analysis, IssueCodes.UnknownContainerAlias).Message, Does.EndWith("The content type has no tabs or groups."));
			Assert.That(Single(analysis, IssueCodes.UnknownContainerAlias).Suggestion, Is.Null);
		});
	}

	[Test]
	public void A_hidden_mandatory_property_is_PV203_whether_hidden_by_alias_or_by_container()
	{
		Mandatory(_site.LandingPage, "relatedLinks");
		Mandatory(_site.LandingPage, "metaTitle");

		ConfigurationAnalysis analysis = Analyze();

		List<ConfigurationAnalysisIssue> issues = analysis.Issues.Where(issue => issue.Code == IssueCodes.HiddenMandatoryProperty).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(2), "campaign hides metaKeywords only, so neither is hidden there");
			Assert.That(issues.Select(issue => issue.SiteLabel), Is.All.EqualTo("corporate"));
			Assert.That(issues.Select(issue => issue.Severity), Is.All.EqualTo(IssueSeverity.Warning));
			Assert.That(issues.Select(issue => issue.Message), Has.One.Contains("Property 'relatedLinks' on content type 'landingPage' is mandatory and hidden by site 'corporate'"));
			Assert.That(issues.Select(issue => issue.Message), Has.One.Contains("Property 'metaTitle' on content type 'landingPage' is mandatory"));
			Assert.That(issues.Select(issue => issue.Path), Is.All.EqualTo("Sites:corporate:ContentTypes:landingPage"));
		});
	}

	[Test]
	public void A_hidden_mandatory_property_from_a_composition_is_PV203()
	{
		// Composed properties are clones made on every read, so the flag goes on the composition's own property.
		Mandatory(_site.SiteSettings, "legacyNotes");
		// The sample's global siteSettings rule would hide the same property on the composition itself.
		_site.Options.ContentTypes.Remove("siteSettings");
		_site.Options.ContentTypes["landingPage"] = Block(containers: ["legacyTab"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.HiddenMandatoryProperty);
		Assert.Multiple(() =>
		{
			Assert.That(issue.SiteLabel, Is.Null);
			Assert.That(issue.Message, Does.Contain("Property 'legacyNotes' on content type 'landingPage' is mandatory and hidden by the global rules"));
		});
	}

	[Test]
	public void Global_entries_are_analyzed_once_and_site_entries_once_per_site()
	{
		_site.Options.ContentTypes["landingPage"] = Block(["missingProperty"]);
		_site.Options.Sites["corporate"].ContentTypes["landingPage"] = Block(["missingProperty"]);
		_site.Options.Sites["campaign"].ContentTypes["landingPage"] = Block(["missingProperty"]);

		ConfigurationAnalysis analysis = Analyze();

		List<ConfigurationAnalysisIssue> issues = analysis.Issues.Where(issue => issue.Code == IssueCodes.UnknownPropertyAlias).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.SiteLabel), Is.EqualTo(new[] { null, "corporate", "campaign" }));
			Assert.That(issues[0].Path, Is.EqualTo("ContentTypes:landingPage:Properties"));
			Assert.That(issues[0].Message, Does.Contain("in the global rules"));
		});
		_site.ContentTypeService.Verify(service => service.GetAll(), Times.Once);
	}

	[Test]
	public void An_entry_keyed_by_a_composition_is_PV205_listing_every_type_composed_of_it_directly_or_transitively()
	{
		// landingPage composes siteSettings; landingChild is created under landingPage (a parent counts as a composition).
		_site.ContentTypes.Add(TestContentType.Document("landingChild").Parent(_site.LandingPage).Group("extra", "extraText").Build());
		_site.Options.Sites["campaign"].ContentTypes["siteSettings"] = Block(["legacyNotes"]);

		ConfigurationAnalysis analysis = Analyze();

		List<ConfigurationAnalysisIssue> issues = analysis.Issues
			.Where(issue => issue.Code == IssueCodes.CompositionRuleReach && issue.ContentTypeAlias == "siteSettings")
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(2), "the global siteSettings entry and campaign's");
			Assert.That(issues.Select(issue => issue.Severity), Is.All.EqualTo(IssueSeverity.Info));
			Assert.That(issues.Select(issue => issue.SiteLabel), Is.EqualTo(new[] { null, "campaign" }));
			Assert.That(issues.Select(issue => issue.ContentTypeAlias), Is.All.EqualTo("siteSettings"));
			Assert.That(issues.Select(issue => issue.Path), Is.EqualTo(new[] { "ContentTypes:siteSettings", "Sites:campaign:ContentTypes:siteSettings" }));
			Assert.That(
				issues[0].Message,
				Is.EqualTo("Content type 'siteSettings' is a composition, so the global rules for it also apply to the 2 content types composed of it: landingChild, landingPage. There they hide only the properties and containers 'siteSettings' contributes."));
			Assert.That(issues[1].Message, Does.Contain("so the rules of site 'campaign' for it also apply to the 2 content types"));
			Assert.That(analysis.IsHealthy, Is.True, "informational only");
		});
	}

	[Test]
	public void A_parent_document_type_entry_is_PV205_for_its_child_types()
	{
		_site.ContentTypes.Add(TestContentType.Document("landingChild").Parent(_site.LandingPage).Build());

		ConfigurationAnalysis analysis = Analyze();

		List<ConfigurationAnalysisIssue> issues = analysis.Issues.Where(issue => issue.Code == IssueCodes.CompositionRuleReach && issue.ContentTypeAlias == "landingPage").ToList();
		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.SiteLabel), Is.EqualTo(new[] { "corporate", "campaign", "everythingElse" }));
			Assert.That(issues.Select(issue => issue.Message), Is.All.Contain("also apply to the 1 content type composed of it: landingChild."));
		});
	}

	[Test]
	public void PV205_lists_ten_types_and_counts_the_rest()
	{
		for (var index = 1; index <= 11; index++)
		{
			_site.ContentTypes.Add(TestContentType.Document($"page{index:D2}").ComposedOf(_site.SiteSettings).Build());
		}

		ConfigurationAnalysis analysis = Analyze();

		Assert.That(
			Single(analysis, IssueCodes.CompositionRuleReach).Message,
			Does.Contain("also apply to the 12 content types composed of it: landingPage, page01, page02, page03, page04, page05, page06, page07, page08, page09 and 2 more."));
	}

	[Test]
	public void An_unknown_alias_in_an_entry_keyed_by_a_composition_is_reported_against_the_composition()
	{
		// title and seoTab exist on landingPage, which composes siteSettings, but not on siteSettings itself.
		_site.Options.ContentTypes["siteSettings"] = Block(["title"], ["seoTab", "legacyTab"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue property = Single(analysis, IssueCodes.UnknownPropertyAlias);
		ConfigurationAnalysisIssue container = Single(analysis, IssueCodes.UnknownContainerAlias);
		Assert.Multiple(() =>
		{
			Assert.That(property.ContentTypeAlias, Is.EqualTo("siteSettings"));
			Assert.That(property.Message, Does.Contain("Property 'title' in the global rules does not exist on content type 'siteSettings'"));
			Assert.That(container.ContentTypeAlias, Is.EqualTo("siteSettings"));
			Assert.That(container.Message, Does.Contain("Container 'seoTab' in the global rules does not exist on content type 'siteSettings'"));
			Assert.That(container.Message, Does.EndWith("Available containers: legacyTab, legacyTab/legacy."));
		});
	}

	[Test]
	public void A_rule_set_no_site_includes_is_PV106()
	{
		_site.Options.RuleSets["simplePages"] = new RuleSetOptions { ContentTypes = { ["landingPage"] = Block(containers: ["seoTab"]) } };
		_site.Options.RuleSets["sharing"] = new RuleSetOptions { ContentTypes = { ["article"] = Block(containers: ["shareTab"]) } };
		_site.Options.Sites["campaign"].Include = ["SHARING"];

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnusedRuleSet);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.Path, Is.EqualTo("RuleSets:simplePages"));
			Assert.That(issue.SiteLabel, Is.Null);
			Assert.That(issue.Message, Is.EqualTo("Rule set 'simplePages' is not included by any site, so its rules never apply. Add it to the Include of the sites that should use it, or remove it."));
		});
	}

	[Test]
	public void Rule_set_entries_are_analyzed_once_however_many_sites_include_the_set()
	{
		_site.Options.RuleSets["simplePages"] = new RuleSetOptions { ContentTypes = { ["landingPage"] = Block(["missingProperty"]) } };
		foreach (SiteVisibilityOptions site in _site.Options.Sites.Values)
		{
			site.Include = ["simplePages"];
		}

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnknownPropertyAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.SiteLabel, Is.Null);
			Assert.That(issue.ContentTypeAlias, Is.EqualTo("landingPage"));
			Assert.That(issue.Path, Is.EqualTo("RuleSets:simplePages:ContentTypes:landingPage:Properties"));
			Assert.That(issue.Message, Does.StartWith("Property 'missingProperty' in rule set 'simplePages' does not exist on content type 'landingPage'"));
		});
	}

	[Test]
	public void A_rule_set_entry_keyed_by_a_composition_is_PV205_naming_the_rule_set()
	{
		_site.Options.RuleSets["legacy"] = new RuleSetOptions { ContentTypes = { ["siteSettings"] = Block(["legacyNotes"]) } };
		_site.Options.Sites["campaign"].Include = ["legacy"];

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = analysis.Issues.Single(i => i.Code == IssueCodes.CompositionRuleReach && i.Path == "RuleSets:legacy:ContentTypes:siteSettings");
		Assert.Multiple(() =>
		{
			Assert.That(issue.SiteLabel, Is.Null);
			Assert.That(issue.Message, Does.StartWith("Content type 'siteSettings' is a composition, so the rules of rule set 'legacy' for it also apply to the 1 content type composed of it: landingPage."));
		});
	}

	[Test]
	public void An_entry_that_hides_nothing_is_PV206_and_is_not_checked_further()
	{
		_site.Options.ContentTypes["article"] = Block();
		_site.Options.RuleSets["simplePages"] = new RuleSetOptions { ContentTypes = { ["promoBanner"] = null! } };
		_site.Options.Sites["campaign"].Include = ["simplePages"];
		_site.Options.Sites["corporate"].ContentTypes["landingPge"] = new ContentTypeVisibilityOptions();

		ConfigurationAnalysis analysis = Analyze();

		List<ConfigurationAnalysisIssue> issues = analysis.Issues.Where(issue => issue.Code == IssueCodes.EmptyContentTypeRule).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(
				issues.Select(issue => issue.Path),
				Is.EqualTo(new[] { "ContentTypes:article", "RuleSets:simplePages:ContentTypes:promoBanner", "Sites:corporate:ContentTypes:landingPge" }));
			Assert.That(issues.Select(issue => issue.Severity), Is.All.EqualTo(IssueSeverity.Info));
			Assert.That(issues.Select(issue => issue.SiteLabel), Is.EqualTo(new[] { null, null, "corporate" }));
			Assert.That(issues.Select(issue => issue.ContentTypeAlias), Is.EqualTo(new[] { "article", "promoBanner", "landingPge" }));
			Assert.That(issues[0].Message, Is.EqualTo("Content type 'article' in the global rules lists no properties and no containers, so it hides nothing. Add aliases, or remove the entry."));
			Assert.That(issues[1].Message, Does.StartWith("Content type 'promoBanner' in rule set 'simplePages' lists no properties"));
			Assert.That(analysis.Issues.Select(issue => issue.Code), Has.None.EqualTo(IssueCodes.UnknownContentTypeAlias), "an empty entry's content type alias is not checked");
			Assert.That(analysis.IsHealthy, Is.True, "informational only");
		});
	}

	[Test]
	public void A_site_without_content_type_entries_is_not_PV206()
	{
		_site.Options.Sites["everythingElse"].ContentTypes.Clear();

		ConfigurationAnalysis analysis = Analyze();

		Assert.That(analysis.Issues.Select(issue => issue.Code), Has.None.EqualTo(IssueCodes.EmptyContentTypeRule));
	}

	[Test]
	public void A_disabled_package_is_PV302_and_the_rules_are_still_analyzed()
	{
		_site.Options.Enabled = false;
		_site.Options.ContentTypes["landingPge"] = Block(["title"]);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.Disabled);
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Info));
			Assert.That(issue.Path, Is.EqualTo("PropertyVisibility:Enabled"));
			Assert.That(analysis.Issues.Select(i => i.Code), Does.Contain(IssueCodes.UnknownContentTypeAlias));
			Assert.That(analysis.Summary.Enabled, Is.False);
		});
	}

	[Test]
	public void A_disabled_package_alone_is_still_healthy()
	{
		_site.Options.Enabled = false;

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.Disabled, IssueCodes.CompositionRuleReach }));
			Assert.That(analysis.IsHealthy, Is.True);
			Assert.That(analysis.InfoCount, Is.EqualTo(2));
		});
	}

	[TestCase(17, 8, 0, IssueSeverity.Info)]
	[TestCase(17, 7, 1, IssueSeverity.Info)]
	[TestCase(17, 6, 3, IssueSeverity.Info)]
	[TestCase(18, 0, 0, IssueSeverity.Warning)]
	[TestCase(16, 4, 0, IssueSeverity.Warning)]
	public void An_untested_Umbraco_version_is_PV303(int major, int minor, int patch, IssueSeverity expected)
	{
		_site.UmbracoVersion = new Version(major, minor, patch);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UntestedUmbracoVersion);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(expected));
			Assert.That(issue.Message, Does.Contain($"Umbraco {major}.{minor}.{patch}"));
			Assert.That(issue.Message, Does.Contain("17.6.2, 17.7.0"));
			Assert.That(analysis.Summary.IsTestedUmbracoVersion, Is.False);
		});
	}

	[TestCase(17, 6, 2)]
	[TestCase(17, 7, 0)]
	public void A_tested_Umbraco_version_is_not_PV303(int major, int minor, int patch)
	{
		_site.UmbracoVersion = new Version(major, minor, patch);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.CompositionRuleReach }));
			Assert.That(analysis.Summary.IsTestedUmbracoVersion, Is.True);
		});
	}

	[Test]
	public void The_Umbraco_version_is_checked_even_when_the_options_are_invalid()
	{
		_site.OptionsFailure = new InvalidOperationException("binder failure");
		_site.UmbracoVersion = new Version(17, 8, 0);

		ConfigurationAnalysis analysis = Analyze();

		Assert.That(analysis.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.ConfigurationInvalid, IssueCodes.UntestedUmbracoVersion }));
	}

	[Test]
	public void An_empty_content_tree_lists_no_roots()
	{
		_site.Roots();
		_site.Options.Sites["corporate"].RootNodeName = null;

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(Single(analysis, IssueCodes.RootNodeKeyNotARoot).Message, Does.EndWith("There are no root nodes."));
			Assert.That(Single(analysis, IssueCodes.RootNodeNameNotFound).Suggestion, Is.Null);
			Assert.That(analysis.Summary.RootNodeKeys, Is.Empty);
		});
	}

	[TestCase("bannerImg", "bannerImage")]
	[TestCase("BANNERIMAGE ", "bannerImage")]
	[TestCase("seo", "seoTab")]
	[TestCase("metaKeyword", "metaKeywords")]
	[TestCase("titel", "title", TestName = "Two swapped characters count as one edit")]
	[TestCase("somethingElse", null)]
	[TestCase("  ", null)]
	[TestCase("url", null, TestName = "A three-letter value allows one edit: url does not suggest seo")]
	[TestCase("x", null, TestName = "A value under three characters is never matched by containment: x does not suggest bodyText")]
	[TestCase("menu", null, TestName = "A four-letter value allows one edit: menu does not suggest meta")]
	[TestCase("logos", "logo")]
	[TestCase("TITLE", "title", TestName = "A value that differs only in case is suggested")]
	public void Suggest_returns_the_closest_candidate(string value, string? expected)
	{
		string[] candidates = ["title", "bannerImage", "relatedLinks", "seoTab", "seo", "meta", "metaKeywords", "metaTitle", "bodyText", "logo"];

		Assert.That(ConfigurationAnalyzer.Suggest(value, candidates.Where(candidate => !string.Equals(candidate, value.Trim(), StringComparison.Ordinal))), Is.EqualTo(expected));
	}

	[TestCase("title", "title", 0)]
	[TestCase("titel", "title", 1)]
	[TestCase("menu", "meta", 2)]
	[TestCase("url", "seo", 3)]
	[TestCase("", "abc", 3)]
	[TestCase("abc", "", 3)]
	[TestCase("ca", "abc", 3, TestName = "Optimal string alignment: a swapped pair is not edited again")]
	public void EditDistance_counts_a_swap_of_neighbours_as_one_edit(string a, string b, int expected)
		=> Assert.That(ConfigurationAnalyzer.EditDistance(a, b), Is.EqualTo(expected));

	[Test]
	public void A_hidden_mandatory_property_in_a_group_whose_name_holds_a_slash_is_PV203_under_a_tab_rule()
	{
		// Group "Image/Video" in tab "Media" has the alias media/image/Video; the tab rule removes it with the tab.
		IContentType mediaPage = MediaPage();
		Mandatory(mediaPage, "coverImage");
		_site.ContentTypes.Add(mediaPage);
		_site.Options.ContentTypes["mediaPage"] = Block(containers: ["media"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.HiddenMandatoryProperty);
		Assert.Multiple(() =>
		{
			Assert.That(issue.ContentTypeAlias, Is.EqualTo("mediaPage"));
			Assert.That(issue.Message, Does.Contain("Property 'coverImage' on content type 'mediaPage' is mandatory and hidden by the global rules"));
		});
	}

	[Test]
	public void PV202_for_a_group_whose_name_holds_a_slash_suggests_an_alias_that_validates_and_hides_the_group()
	{
		IContentType mediaPage = MediaPage();
		_site.ContentTypes.Add(mediaPage);
		_site.Options.ContentTypes["mediaPage"] = Block(containers: ["media/imageVideo"]);

		ConfigurationAnalysis before = Analyze();

		ConfigurationAnalysisIssue issue = Single(before, IssueCodes.UnknownContainerAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Message, Does.EndWith("Available containers: media, media/details, media/image/Video."));
			Assert.That(issue.Suggestion, Is.EqualTo("media/image/Video"));
			Assert.That(PropertyVisibilityOptionsValidator.IsValidContainerAlias(issue.Suggestion), Is.True, "applying the suggestion must not make the configuration invalid (PV006, then PV008)");
		});

		// Apply the suggestion.
		_site.Options.ContentTypes["mediaPage"] = Block(containers: [issue.Suggestion!]);
		ConfigurationAnalysis after = Analyze();
		HiddenFields hidden = new HiddenFieldsResolver().Resolve(mediaPage, [_site.Options.ContentTypes["mediaPage"]], hideEmptiedContainers: false);

		Assert.Multiple(() =>
		{
			Assert.That(after.Issues.Select(i => i.Code), Has.None.EqualTo(IssueCodes.UnknownContainerAlias));
			Assert.That(after.Issues.Select(i => i.Code), Has.None.EqualTo(IssueCodes.InvalidContainerAlias));
			Assert.That(new PropertyVisibilityOptionsValidator().Validate(null, _site.Options).Succeeded, Is.True);
			Assert.That(hidden.ContainerKeys, Is.EquivalentTo(new[] { TestContentType.ContainerKey(mediaPage, "media/image/Video") }));
		});
	}

	[Test]
	public void PV202_never_lists_or_suggests_an_alias_the_validator_rejects()
	{
		// A root-level group named "/extras" gets the alias /extras, which a rule cannot name (PV006).
		IContentType page = TestContentType.Document("extrasPage")
			.NamedGroup("/extras", "/extras", "extraText")
			.NamedGroup("notes", "Notes", "notesText")
			.Build();
		_site.ContentTypes.Add(page);
		_site.Options.ContentTypes["extrasPage"] = Block(containers: ["extras"]);

		ConfigurationAnalysis analysis = Analyze();

		ConfigurationAnalysisIssue issue = Single(analysis, IssueCodes.UnknownContainerAlias);
		Assert.Multiple(() =>
		{
			Assert.That(issue.Message, Does.EndWith("Available containers: notes."));
			Assert.That(issue.Suggestion, Is.Null);
		});
	}

	[Test]
	public void A_validation_failure_for_a_site_label_with_parentheses_keeps_its_detail_lines()
	{
		var invalid = new PropertyVisibilityOptions
		{
			Sites =
			{
				["Corporate (old)"] = new SiteVisibilityOptions(),
				["Campaign (2026)"] = new SiteVisibilityOptions { ContentTypes = { ["landingPage"] = Block(containers: ["/seo"]) }, IsDefault = true },
			},
		};
		ValidateOptionsResult result = new PropertyVisibilityOptionsValidator().Validate(Microsoft.Extensions.Options.Options.DefaultName, invalid);
		_site.OptionsFailure = new OptionsValidationException(Microsoft.Extensions.Options.Options.DefaultName, typeof(PropertyVisibilityOptions), result.Failures);

		ConfigurationAnalysis analysis = Analyze();

		Assert.Multiple(() =>
		{
			Assert.That(
				analysis.Issues.Select(issue => issue.Code),
				Is.EquivalentTo(new[] { IssueCodes.ConfigurationInvalid, IssueCodes.SiteWithoutIdentity, IssueCodes.InvalidContainerAlias }));
			Assert.That(Single(analysis, IssueCodes.ConfigurationInvalid).Message, Does.EndWith("2 validation errors are listed separately."));
			Assert.That(Single(analysis, IssueCodes.SiteWithoutIdentity).SiteLabel, Is.EqualTo("Corporate (old)"));
			Assert.That(Single(analysis, IssueCodes.SiteWithoutIdentity).Path, Is.EqualTo("Sites:Corporate (old)"));
			Assert.That(Single(analysis, IssueCodes.InvalidContainerAlias).SiteLabel, Is.EqualTo("Campaign (2026)"));
			Assert.That(Single(analysis, IssueCodes.InvalidContainerAlias).Path, Is.EqualTo("Sites:Campaign (2026):ContentTypes:landingPage:Containers"));
		});
	}

	// Tab "Media" with group "Details" (caption) and group "Image/Video" (coverImage), aliased as Umbraco 17 does.
	private static IContentType MediaPage() => TestContentType.Document("mediaPage")
		.NamedTab("media", "Media")
		.NamedGroup("media/details", "Details", "caption")
		.NamedGroup("media/image/Video", "Image/Video", "coverImage")
		.Build();

	private static ConfigurationAnalysisIssue Single(ConfigurationAnalysis analysis, string code)
	{
		List<ConfigurationAnalysisIssue> matches = analysis.Issues.Where(issue => issue.Code == code).ToList();
		Assert.That(matches, Has.Count.EqualTo(1), $"expected exactly one {code}; got: {string.Join(" | ", analysis.Issues)}");
		return matches[0];
	}

	// The codes without the sample's informational PV205 (its global siteSettings entry reaches landingPage), for the
	// tests about sites and roots.
	private static IEnumerable<string> SiteIssueCodes(ConfigurationAnalysis analysis)
		=> analysis.Issues.Select(issue => issue.Code).Where(code => code != IssueCodes.CompositionRuleReach);

	private ConfigurationAnalysis Analyze() => _site.CreateAnalyzer().Analyze();
}
