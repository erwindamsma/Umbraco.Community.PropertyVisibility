using Microsoft.Extensions.Options;
using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

[TestFixture]
public sealed class PropertyVisibilityOptionsValidatorTests
{
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");

	private readonly PropertyVisibilityOptionsValidator _validator = new();

	[Test]
	public void Default_options_are_valid()
	{
		var options = new PropertyVisibilityOptions();

		Assert.Multiple(() =>
		{
			Assert.That(_validator.Collect(options), Is.Empty);
			Assert.That(_validator.Validate(Options.DefaultName, options).Succeeded, Is.True);
		});
	}

	[Test]
	public void Multi_site_sample_is_valid()
	{
		PropertyVisibilityOptions options = SampleOptions();

		Assert.Multiple(() =>
		{
			Assert.That(_validator.Collect(options), Is.Empty);
			Assert.That(_validator.Validate(Options.DefaultName, options).Succeeded, Is.True);
		});
	}

	[Test]
	public void Null_options_fail()
	{
		ValidateOptionsResult result = _validator.Validate(Options.DefaultName, null!);

		Assert.That(result.Failed, Is.True);
	}

	[Test]
	public void Site_without_identity_is_PV003()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["orphan"] = new SiteVisibilityOptions();

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.SiteWithoutIdentity));
			Assert.That(issue.Code, Is.EqualTo("PV003"));
			Assert.That(issue.Path, Is.EqualTo("Sites:orphan"));
		});
	}

	[Test]
	public void Empty_key_and_blank_name_count_as_no_identity()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["orphan"] = new SiteVisibilityOptions { RootNodeKey = Guid.Empty, RootNodeName = "   " };

		Assert.That(SingleError(options).Code, Is.EqualTo(IssueCodes.SiteWithoutIdentity));
	}

	[Test]
	public void Default_flag_alone_is_an_identity()
	{
		var options = new PropertyVisibilityOptions();
		options.Sites["everythingElse"] = new SiteVisibilityOptions { IsDefault = true };

		Assert.That(_validator.Collect(options), Is.Empty);
	}

	[Test]
	public void Duplicate_root_node_key_is_PV004()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["copy"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.DuplicateSiteIdentity));
			Assert.That(issue.Code, Is.EqualTo("PV004"));
			Assert.That(issue.Path, Is.EqualTo("Sites:copy"));
			Assert.That(issue.Message, Does.Contain("corporate").And.Contain(CorporateRoot.ToString()));
		});
	}

	[Test]
	public void Duplicate_root_node_name_ignoring_case_and_whitespace_is_PV004()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["copy"] = new SiteVisibilityOptions { RootNodeName = "  campaign SITE " };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.DuplicateSiteIdentity));
			Assert.That(issue.Message, Does.Contain("campaign").And.Contain("copy"));
		});
	}

	[Test]
	public void More_than_one_default_is_PV005()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["secondDefault"] = new SiteVisibilityOptions { IsDefault = true };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.MultipleDefaultSites));
			Assert.That(issue.Code, Is.EqualTo("PV005"));
			Assert.That(issue.Message, Does.Contain("'everythingElse'").And.Contain("'secondDefault'"));
		});
	}

	[TestCase("/group")]
	[TestCase("tab/")]
	[TestCase("/")]
	[TestCase("//")]
	[TestCase("")]
	[TestCase("   ")]
	public void Invalid_global_container_alias_is_PV006(string alias)
	{
		var options = new PropertyVisibilityOptions();
		options.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Containers = ["seoTab", alias] };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidContainerAlias));
			Assert.That(issue.Code, Is.EqualTo("PV006"));
			Assert.That(issue.Path, Is.EqualTo("ContentTypes:landingPage:Containers"));
		});
	}

	[Test]
	public void Invalid_site_container_alias_is_PV006_with_the_site_path()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["campaign"].ContentTypes["callToAction"] = new ContentTypeVisibilityOptions { Containers = ["settingsTab/"] };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidContainerAlias));
			Assert.That(issue.Path, Is.EqualTo("Sites:campaign:ContentTypes:callToAction:Containers"));
		});
	}

	[TestCase("seoTab", ExpectedResult = true)]
	[TestCase("settingsTab/advanced", ExpectedResult = true)]
	[TestCase("media/image/Video", ExpectedResult = true, TestName = "A group whose name holds a slash: split at the first slash")]
	[TestCase("header/Footer", ExpectedResult = true, TestName = "A tab whose name holds a slash")]
	[TestCase("media//x", ExpectedResult = true, TestName = "A group named '/x': the rest after the first slash is not empty")]
	[TestCase("/group", ExpectedResult = false)]
	[TestCase("tab/", ExpectedResult = false)]
	[TestCase("", ExpectedResult = false)]
	[TestCase(" ", ExpectedResult = false)]
	[TestCase(null, ExpectedResult = false)]
	public bool Container_alias_grammar(string? alias) => PropertyVisibilityOptionsValidator.IsValidContainerAlias(alias);

	[Test]
	public void Site_label_with_a_colon_is_PV007()
	{
		var options = new PropertyVisibilityOptions();
		options.Sites["corporate:nl"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot };

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidSiteLabel));
			Assert.That(issue.Code, Is.EqualTo("PV007"));
			Assert.That(issue.Path, Is.EqualTo("Sites:corporate:nl"));
		});
	}

	[Test]
	public void Included_rule_sets_that_exist_are_valid_whatever_the_case_of_the_name()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.RuleSets["simplePages"] = new RuleSetOptions
		{
			ContentTypes = { ["landingPage"] = new ContentTypeVisibilityOptions { Containers = ["seoTab", "settingsTab/advanced"] } },
		};
		options.Sites["corporate"].Include = ["simplePages"];
		options.Sites["campaign"].Include = ["SIMPLEPAGES", "simplePages"];

		Assert.Multiple(() =>
		{
			Assert.That(_validator.Collect(options), Is.Empty);
			Assert.That(_validator.Validate(Options.DefaultName, options).Succeeded, Is.True);
		});
	}

	[Test]
	public void An_included_rule_set_that_does_not_exist_is_PV009_suggesting_the_closest_name()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.RuleSets["simplePages"] = new RuleSetOptions();
		options.Sites["corporate"].Include = ["simplePages"];
		options.Sites["campaign"].Include = ["simplePage"];

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.UnknownRuleSet));
			Assert.That(issue.Code, Is.EqualTo("PV009"));
			Assert.That(issue.Path, Is.EqualTo("Sites:campaign:Include"));
			Assert.That(issue.Message, Is.EqualTo("Site 'campaign' includes the rule set 'simplePage', which does not exist under RuleSets."));
			Assert.That(issue.Suggestion, Is.EqualTo("simplePages"));
		});
	}

	[Test]
	public void Each_include_without_a_rule_set_is_its_own_PV009()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.Sites["corporate"].Include = ["first", "second"];

		IReadOnlyList<ConfigurationIssue> issues = _validator.Collect(options);

		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.UnknownRuleSet, IssueCodes.UnknownRuleSet }));
			Assert.That(issues.Select(issue => issue.Suggestion), Is.All.Null, "no rule set to suggest");
		});
	}

	[Test]
	public void Invalid_rule_set_container_alias_is_PV006_with_the_rule_set_path()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.RuleSets["simplePages"] = new RuleSetOptions
		{
			ContentTypes = { ["landingPage"] = new ContentTypeVisibilityOptions { Containers = ["seoTab/"] } },
		};
		options.Sites["corporate"].Include = ["simplePages"];

		ConfigurationIssue issue = SingleError(options);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidContainerAlias));
			Assert.That(issue.Path, Is.EqualTo("RuleSets:simplePages:ContentTypes:landingPage:Containers"));
		});
	}

	[Test]
	public void Null_entries_are_not_structural_errors()
	{
		PropertyVisibilityOptions options = SampleOptions();
		options.ContentTypes["article"] = null!;
		options.RuleSets["empty"] = null!;
		options.Sites["corporate"].Include = ["empty"];

		Assert.That(_validator.Collect(options), Is.Empty);
	}

	[Test]
	public void Every_structural_error_is_collected_and_fails_validation()
	{
		var options = new PropertyVisibilityOptions();
		options.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Containers = ["/a"] };
		options.Sites["first"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, IsDefault = true };
		options.Sites["second"] = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, IsDefault = true };
		options.Sites["third"] = new SiteVisibilityOptions();
		options.Sites["with:colon"] = new SiteVisibilityOptions { RootNodeName = "Campaign site" };
		options.Sites["fourth"] = new SiteVisibilityOptions { RootNodeName = "Other site", Include = ["missing"] };

		IReadOnlyList<ConfigurationIssue> issues = _validator.Collect(options);
		ValidateOptionsResult result = _validator.Validate(Options.DefaultName, options);

		Assert.Multiple(() =>
		{
			Assert.That(
				issues.Select(issue => issue.Code),
				Is.EquivalentTo(new[]
				{
					IssueCodes.InvalidContainerAlias,
					IssueCodes.DuplicateSiteIdentity,
					IssueCodes.SiteWithoutIdentity,
					IssueCodes.InvalidSiteLabel,
					IssueCodes.UnknownRuleSet,
					IssueCodes.MultipleDefaultSites,
				}));
			Assert.That(issues.Select(issue => issue.Severity), Has.All.EqualTo(IssueSeverity.Error));
			Assert.That(result.Failed, Is.True);
			Assert.That(result.Failures, Has.Exactly(issues.Count).Items);
			Assert.That(result.Failures, Has.One.StartsWith("PV007 (Sites:with:colon): "));
		});
	}

	[Test]
	public void Issue_formats_with_and_without_path()
	{
		Assert.Multiple(() =>
		{
			Assert.That(new ConfigurationIssue("PV003", IssueSeverity.Error, "message", "Sites:x").ToString(), Is.EqualTo("PV003 (Sites:x): message"));
			Assert.That(new ConfigurationIssue("PV302", IssueSeverity.Info, "message").ToString(), Is.EqualTo("PV302: message"));
		});
	}

	private ConfigurationIssue SingleError(PropertyVisibilityOptions options)
	{
		IReadOnlyList<ConfigurationIssue> issues = _validator.Collect(options);
		ValidateOptionsResult result = _validator.Validate(Options.DefaultName, options);

		Assert.That(issues, Has.Count.EqualTo(1), string.Join(Environment.NewLine, issues));
		Assert.That(issues[0].Severity, Is.EqualTo(IssueSeverity.Error));
		Assert.That(result.Failed, Is.True);
		Assert.That(result.Failures, Is.EqualTo(new[] { issues[0].ToString() }));
		return issues[0];
	}

	// The multi-site sample of the configuration docs; structurally valid.
	private static PropertyVisibilityOptions SampleOptions()
	{
		var options = new PropertyVisibilityOptions();
		options.ContentTypes["siteSettings"] = new ContentTypeVisibilityOptions { Containers = ["legacyTab"] };

		var corporate = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" };
		corporate.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions
		{
			Properties = ["bannerImage", "relatedLinks"],
			Containers = ["seoTab", "settingsTab/advanced"],
		};
		corporate.ContentTypes["article"] = new ContentTypeVisibilityOptions { Containers = ["shareTab"] };
		corporate.ContentTypes["promoBanner"] = new ContentTypeVisibilityOptions { Properties = ["overlayColour"] };
		corporate.ContentTypes["promoBannerSettings"] = new ContentTypeVisibilityOptions { Properties = ["anchorId"] };

		var campaign = new SiteVisibilityOptions { RootNodeName = "Campaign site" };
		campaign.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["metaKeywords"] };
		campaign.ContentTypes["callToAction"] = new ContentTypeVisibilityOptions { Containers = ["settingsTab"] };

		var everythingElse = new SiteVisibilityOptions { IsDefault = true };
		everythingElse.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["bannerImage"] };

		options.Sites["corporate"] = corporate;
		options.Sites["campaign"] = campaign;
		options.Sites["everythingElse"] = everythingElse;
		return options;
	}
}
