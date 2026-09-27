using Moq;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

[TestFixture]
public sealed class SiteMatcherTests
{
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid CampaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");
	private static readonly Guid OtherRoot = Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");

	private Mock<IRootNodeResolver> _rootNodeResolver = null!;
	private SiteMatcher _matcher = null!;

	[SetUp]
	public void SetUp()
	{
		_rootNodeResolver = new Mock<IRootNodeResolver>(MockBehavior.Strict);
		_matcher = new SiteMatcher(_rootNodeResolver.Object);
	}

	[Test]
	public void No_sites_matches_nothing_without_reading_names()
	{
		SiteMatch match = _matcher.Match(new PropertyVisibilityOptions(), Document(CorporateRoot));

		Assert.That(match, Is.SameAs(SiteMatch.None));
		_rootNodeResolver.VerifyNoOtherCalls();
	}

	[Test]
	public void Key_match_wins_over_a_name_match_on_another_site()
	{
		RootName(CorporateRoot, "Campaign site");
		PropertyVisibilityOptions options = Options(
			("byName", new SiteVisibilityOptions { RootNodeName = "Campaign site" }),
			("byKey", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }));

		SiteMatch match = _matcher.Match(options, Document(CorporateRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Label, Is.EqualTo("byKey"));
			Assert.That(match.Site, Is.SameAs(options.Sites["byKey"]));
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(match.Issues, Is.Empty);
		});
	}

	[Test]
	public void Name_match_is_case_insensitive_and_trimmed()
	{
		RootName(CampaignRoot, "  Campaign site ");
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }),
			("campaign", new SiteVisibilityOptions { RootNodeName = " CAMPAIGN SITE  " }));

		SiteMatch match = _matcher.Match(options, Document(CampaignRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Label, Is.EqualTo("campaign"));
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Name));
			Assert.That(match.Issues, Is.Empty);
		});
	}

	[Test]
	public void Name_match_works_for_a_root_resolved_from_the_parent()
	{
		RootName(CampaignRoot, "Campaign site");
		PropertyVisibilityOptions options = Options(("campaign", new SiteVisibilityOptions { RootNodeName = "Campaign site" }));

		SiteMatch match = _matcher.Match(options, new RootNodeResolution(CampaignRoot, RootResolutionSource.Parent));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Name));
	}

	[Test]
	public void Default_site_is_the_last_resort()
	{
		RootName(OtherRoot, "Some other site");
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }),
			("campaign", new SiteVisibilityOptions { RootNodeName = "Campaign site" }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch match = _matcher.Match(options, Document(OtherRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Label, Is.EqualTo("everythingElse"));
			Assert.That(match.Site, Is.SameAs(options.Sites["everythingElse"]));
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Default));
		});
	}

	[Test]
	public void Nothing_matches_when_no_tier_applies()
	{
		RootName(OtherRoot, "Some other site");
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }),
			("campaign", new SiteVisibilityOptions { RootNodeName = "Campaign site" }));

		SiteMatch match = _matcher.Match(options, Document(OtherRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.None));
			Assert.That(match.Label, Is.Null);
			Assert.That(match.Site, Is.Null);
		});
	}

	[Test]
	public void Unreadable_root_name_falls_through_to_the_default()
	{
		RootName(OtherRoot, null);
		PropertyVisibilityOptions options = Options(
			("campaign", new SiteVisibilityOptions { RootNodeName = "Campaign site" }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch match = _matcher.Match(options, Document(OtherRoot));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Default));
	}

	[Test]
	public void Names_are_not_read_when_no_site_is_configured_by_name()
	{
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch match = _matcher.Match(options, Document(OtherRoot));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Default));
		_rootNodeResolver.Verify(resolver => resolver.GetRootName(It.IsAny<Guid>()), Times.Never);
	}

	[Test]
	public void Key_match_with_a_drifted_name_carries_a_drift_warning()
	{
		RootName(CorporateRoot, "Corporate website");
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" }));

		SiteMatch match = _matcher.Match(options, Document(CorporateRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Label, Is.EqualTo("corporate"));
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(match.Issues, Has.Count.EqualTo(1));
			Assert.That(match.Issues[0].Code, Is.EqualTo(IssueCodes.RootNodeNameDrift));
			Assert.That(match.Issues[0].Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(match.Issues[0].Path, Is.EqualTo("Sites:corporate:RootNodeName"));
			Assert.That(match.Issues[0].Message, Does.Contain("Corporate site").And.Contain("Corporate website"));
		});
	}

	[Test]
	public void Key_match_with_the_same_name_in_another_case_has_no_drift_warning()
	{
		RootName(CorporateRoot, "corporate SITE");
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = " Corporate site " }));

		SiteMatch match = _matcher.Match(options, Document(CorporateRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(match.Issues, Is.Empty);
		});
	}

	[Test]
	public void A_site_matched_by_key_does_not_take_another_root_by_its_stale_name()
	{
		// The roots were renamed or swapped: corporate's configured name is now the campaign root's name.
		RootName(CorporateRoot, "Corporate site");
		RootName(CampaignRoot, "Campaign site");
		_rootNodeResolver.Setup(resolver => resolver.IsRoot(CorporateRoot)).Returns(true);
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Campaign site" }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch campaign = _matcher.Match(options, Document(CampaignRoot));
		SiteMatch corporate = _matcher.Match(options, Document(CorporateRoot));

		Assert.Multiple(() =>
		{
			Assert.That(campaign.Label, Is.EqualTo("everythingElse"));
			Assert.That(campaign.Reason, Is.EqualTo(SiteMatchReason.Default));
			Assert.That(corporate.Label, Is.EqualTo("corporate"));
			Assert.That(corporate.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(corporate.Issues.Single().Code, Is.EqualTo(IssueCodes.RootNodeNameDrift));
		});
	}

	[Test]
	public void A_site_matched_by_key_leaves_another_root_with_its_name_unmatched_without_a_default()
	{
		RootName(CampaignRoot, "Campaign site");
		_rootNodeResolver.Setup(resolver => resolver.IsRoot(CorporateRoot)).Returns(true);
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Campaign site" }));

		SiteMatch match = _matcher.Match(options, Document(CampaignRoot));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.None));
	}

	[Test]
	public void A_site_whose_key_is_not_a_root_still_matches_by_name()
	{
		// PV101: the key points below a root (or nowhere); the name keeps the site working.
		var pageBelowRoot = Guid.Parse("1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b");
		RootName(CorporateRoot, "Corporate site");
		_rootNodeResolver.Setup(resolver => resolver.IsRoot(pageBelowRoot)).Returns(false);
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = pageBelowRoot, RootNodeName = "Corporate site" }));

		SiteMatch match = _matcher.Match(options, Document(CorporateRoot));

		Assert.Multiple(() =>
		{
			Assert.That(match.Label, Is.EqualTo("corporate"));
			Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Name));
		});
	}

	[Test]
	public void Key_only_site_does_not_read_the_root_name()
	{
		PropertyVisibilityOptions options = Options(("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }));

		SiteMatch match = _matcher.Match(options, Document(CorporateRoot));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Key));
		_rootNodeResolver.Verify(resolver => resolver.GetRootName(It.IsAny<Guid>()), Times.Never);
	}

	[Test]
	public void Resolution_source_none_skips_key_and_name_tiers_and_uses_the_default()
	{
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch match = _matcher.Match(options, RootNodeResolution.None);

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.Default));
		_rootNodeResolver.VerifyNoOtherCalls();
	}

	[Test]
	public void Resolution_source_none_without_a_default_matches_nothing()
	{
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" }));

		SiteMatch match = _matcher.Match(options, RootNodeResolution.None);

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.None));
		_rootNodeResolver.VerifyNoOtherCalls();
	}

	[Test]
	public void Recycle_bin_skips_every_tier_including_the_default()
	{
		PropertyVisibilityOptions options = Options(
			("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" }),
			("everythingElse", new SiteVisibilityOptions { IsDefault = true }));

		SiteMatch match = _matcher.Match(options, RootNodeResolution.RecycleBin);

		Assert.That(match, Is.SameAs(SiteMatch.None));
		_rootNodeResolver.VerifyNoOtherCalls();
	}

	[Test]
	public void Recycle_bin_with_a_root_key_still_matches_nothing()
	{
		// Defensive: even if a resolver ever reported a key for trashed content, the bin gets global rules only.
		PropertyVisibilityOptions options = Options(("corporate", new SiteVisibilityOptions { RootNodeKey = CorporateRoot }));

		SiteMatch match = _matcher.Match(options, new RootNodeResolution(CorporateRoot, RootResolutionSource.RecycleBin));

		Assert.That(match.Reason, Is.EqualTo(SiteMatchReason.None));
	}

	private static RootNodeResolution Document(Guid rootKey) => new(rootKey, RootResolutionSource.Document);

	private static PropertyVisibilityOptions Options(params (string Label, SiteVisibilityOptions Site)[] sites)
	{
		var options = new PropertyVisibilityOptions();
		foreach ((var label, SiteVisibilityOptions site) in sites)
		{
			options.Sites.Add(label, site);
		}

		return options;
	}

	private void RootName(Guid rootKey, string? name)
		=> _rootNodeResolver.Setup(resolver => resolver.GetRootName(rootKey)).Returns(name);
}
