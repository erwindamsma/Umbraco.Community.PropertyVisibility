using Microsoft.Extensions.Options;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

/// <summary>
///     Binding real appsettings JSON through the package's options registration. Without
///     <c>ErrorOnUnknownConfiguration</c> the binder silently drops a site whose values cannot be converted, and its
///     documents quietly match another site; these tests pin that such mistakes surface as exceptions instead (which the
///     service logs as <c>PV008</c> and fails open on).
/// </summary>
[TestFixture]
public sealed class PropertyVisibilityOptionsBindingTests
{
	private const string CorporateKey = "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b";

	/// <summary>The multi-site sample of the configuration docs, trimmed to three sites.</summary>
	internal const string Sample = $$"""
		{
			"Enabled": true,
			"ConfigFile": "PropertyVisibility.config.json",
			"HideEmptiedContainers": true,
			"ContentTypes": {
				"siteSettings": { "Containers": ["legacyTab"] }
			},
			"Sites": {
				"corporate": {
					"RootNodeKey": "{{CorporateKey}}",
					"RootNodeName": "Corporate site",
					"ContentTypes": {
						"landingPage": { "Properties": ["bannerImage", "relatedLinks"], "Containers": ["seoTab", "settingsTab/advanced"] }
					}
				},
				"campaign": {
					"RootNodeName": "Campaign site",
					"ContentTypes": { "landingPage": { "Properties": ["metaKeywords"] } }
				},
				"everythingElse": {
					"IsDefault": true,
					"ContentTypes": { "landingPage": { "Properties": ["bannerImage"] } }
				}
			}
		}
		""";

	[Test]
	public void The_sample_binds_completely()
	{
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings(Sample));

		PropertyVisibilityOptions options = configuration.Monitor.CurrentValue;

		Assert.Multiple(() =>
		{
			Assert.That(options.Enabled, Is.True);
			Assert.That(options.HideEmptiedContainers, Is.True);
			Assert.That(options.ContentTypes["siteSettings"].Containers, Is.EqualTo(new[] { "legacyTab" }));
			Assert.That(options.Sites.Keys, Is.EquivalentTo(new[] { "corporate", "campaign", "everythingElse" }));
			Assert.That(options.Sites["corporate"].RootNodeKey, Is.EqualTo(Guid.Parse(CorporateKey)));
			Assert.That(options.Sites["corporate"].RootNodeName, Is.EqualTo("Corporate site"));
			Assert.That(options.Sites["corporate"].ContentTypes["landingPage"].Properties, Is.EqualTo(new[] { "bannerImage", "relatedLinks" }));
			Assert.That(options.Sites["corporate"].ContentTypes["landingPage"].Containers, Is.EqualTo(new[] { "seoTab", "settingsTab/advanced" }));
			Assert.That(options.Sites["campaign"].RootNodeKey, Is.Null);
			Assert.That(options.Sites["everythingElse"].IsDefault, Is.True);
		});
	}

	[Test]
	public void Keys_bind_case_insensitively()
	{
		const string camelCase = """
			{
				"enabled": false,
				"hideEmptiedContainers": false,
				"sites": {
					"campaign": { "rootNodeName": "Campaign site", "contentTypes": { "landingPage": { "properties": ["metaKeywords"] } } }
				}
			}
			""";
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings(camelCase));

		PropertyVisibilityOptions options = configuration.Monitor.CurrentValue;

		Assert.Multiple(() =>
		{
			Assert.That(options.Enabled, Is.False);
			Assert.That(options.HideEmptiedContainers, Is.False);
			Assert.That(options.Sites["campaign"].RootNodeName, Is.EqualTo("Campaign site"));
			Assert.That(options.Sites["campaign"].ContentTypes["landingPage"].Properties, Is.EqualTo(new[] { "metaKeywords" }));
		});
	}

	[Test]
	public void A_missing_section_binds_the_defaults()
	{
		using var configuration = new JsonConfiguration("{}");

		PropertyVisibilityOptions options = configuration.Monitor.CurrentValue;

		Assert.Multiple(() =>
		{
			Assert.That(options.Enabled, Is.True);
			Assert.That(options.ContentTypes, Is.Empty);
			Assert.That(options.Sites, Is.Empty);
		});
	}

	[TestCase("\"RootNodeKey\": \"5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b\"", "\"RootNodeKey\": \"5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7\"", TestName = "RootNodeKey with a missing hex digit")]
	[TestCase("\"IsDefault\": true", "\"IsDefault\": \"yes\"", TestName = "IsDefault that is not a boolean")]
	[TestCase("\"Properties\": [\"metaKeywords\"]", "\"Properties\": \"metaKeywords\"", TestName = "Properties given as a string instead of an array")]
	[TestCase("\"RootNodeName\": \"Campaign site\"", "\"RootNodeNme\": \"Campaign site\"", TestName = "Misspelled key inside a site")]
	[TestCase("\"HideEmptiedContainers\": true", "\"HideEmptiedContainers\": \"ja\"", TestName = "HideEmptiedContainers that is not a boolean")]
	[TestCase("\"Enabled\": true", "\"Enabled\": \"nope\"", TestName = "Enabled that is not a boolean")]
	[TestCase("\"ConfigFile\":", "\"ConfigFiel\":", TestName = "Misspelled top-level key")]
	public void A_value_that_cannot_be_bound_throws_instead_of_dropping_the_site(string original, string mistake)
	{
		Assert.That(Sample, Does.Contain(original), "precondition: the sample contains the text to break");

		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings(ReplaceFirst(Sample, original, mistake)));

		Assert.That(() => configuration.Monitor.CurrentValue, Throws.InvalidOperationException);
	}

	[Test]
	public void A_structural_error_fails_validation()
	{
		const string siteWithoutIdentity = """{ "Sites": { "corporate": { "ContentTypes": {} } } }""";
		using var configuration = new JsonConfiguration(JsonConfiguration.Appsettings(siteWithoutIdentity));

		OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(() => _ = configuration.Monitor.CurrentValue);

		Assert.That(exception!.Failures, Has.One.StartsWith(IssueCodes.SiteWithoutIdentity));
	}

	private static string ReplaceFirst(string text, string oldValue, string newValue)
	{
		var index = text.IndexOf(oldValue, StringComparison.Ordinal);
		return text[..index] + newValue + text[(index + oldValue.Length)..];
	}
}
