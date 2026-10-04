using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

/// <summary>
///     <see cref="AliasSchemaBuilder" />: the rules file schema with the site's aliases and root nodes, checked by
///     validating rules files against it the way an editor does.
/// </summary>
[TestFixture]
public sealed class AliasSchemaBuilderTests
{
	private const string BaseSchemaFile = "src/Umbraco.Community.PropertyVisibility/PropertyVisibility.config-schema.json";

	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid CampaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");

	private IContentType _siteSettings = null!;
	private IContentType _landingPage = null!;
	private IContentType _promoBanner = null!;
	private IContentType _emptyElement = null!;
	private AliasSchemaRoot[] _roots = null!;
	private string _schema = null!;

	[SetUp]
	public void SetUp()
	{
		_siteSettings = Document("siteSettings")
			.NamedTab("legacyTab", "Legacy tab")
			.NamedGroup("legacyTab/legacy", "Legacy", "legacyNotes")
			.Build();
		_landingPage = Document("landingPage")
			.NamedTab("contentTab", "Content")
			.NamedGroup("contentTab/main", "Main", "title", "bannerImage")
			.NamedTab("seoTab", "Seo tab")
			.NamedGroup("seoTab/meta", "Meta", "metaKeywords")
			.ComposedOf(_siteSettings)
			.Build();
		_landingPage.PropertyTypes.Single(property => property.Alias == "title").Mandatory = true;
		_promoBanner = Element("promoBanner").NamedGroup("main", "Main", "headline", "overlayColour").Build();
		_emptyElement = Element("emptyElement").Build();
		_roots = [new AliasSchemaRoot(CorporateRoot, "Corporate site"), new AliasSchemaRoot(CampaignRoot, "Campaign site")];

		_schema = AliasSchemaBuilder.Build([_promoBanner, _landingPage, _siteSettings, _emptyElement], _roots);
	}

	[Test]
	public void A_rules_file_with_the_sites_aliases_and_root_nodes_is_valid()
	{
		var rules = $$"""
			{
				"$schema": "./PropertyVisibility.config-schema.local.json",
				"HideEmptiedContainers": true,
				"ContentTypes": {
					"siteSettings": { "Containers": ["legacyTab"] }
				},
				"RuleSets": {
					"simplePages": { "ContentTypes": { "landingPage": { "Containers": ["seoTab", "contentTab/main"] } } }
				},
				"Sites": {
					"corporate": {
						"RootNodeKey": "{{CorporateRoot}}",
						"Include": ["simplePages", "anything"],
						"ContentTypes": {
							"landingPage": { "Properties": ["bannerImage", "legacyNotes"], "Containers": ["legacyTab/legacy"] },
							"promoBanner": { "Properties": ["overlayColour"] },
							"emptyElement": { "Properties": [] }
						}
					},
					"campaign": { "RootNodeName": "Campaign site" },
					"everythingElse": { "IsDefault": true }
				}
			}
			""";

		Assert.That(JsonSchemaValidation.Validate(_schema, rules), Is.Empty);
	}

	[TestCase("""{ "ContentTypes": { "landingPge": { "Properties": ["title"] } } }""", Description = "unknown content type, top level")]
	[TestCase("""{ "RuleSets": { "set": { "ContentTypes": { "landingPge": {} } } } }""", Description = "unknown content type, rule set")]
	[TestCase("""{ "Sites": { "s": { "IsDefault": true, "ContentTypes": { "landingPge": {} } } } }""", Description = "unknown content type, site")]
	[TestCase("""{ "ContentTypes": { "landingPage": { "Properties": ["bannerImg"] } } }""", Description = "unknown property")]
	[TestCase("""{ "ContentTypes": { "landingPage": { "Containers": ["seoTab/met"] } } }""", Description = "unknown container")]
	[TestCase("""{ "ContentTypes": { "landingPage": { "Containers": ["main"] } } }""", Description = "a container of another content type")]
	[TestCase("""{ "ContentTypes": { "promoBanner": { "Properties": ["title"] } } }""", Description = "a property of another content type")]
	[TestCase("""{ "ContentTypes": { "LandingPage": {} } }""", Description = "other casing: the package accepts it, the schema asks for the exact alias")]
	[TestCase("""{ "ContentTypes": { "emptyElement": { "Properties": ["anything"] } } }""", Description = "a content type without properties")]
	[TestCase("""{ "Sites": { "s": { "RootNodeKey": "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d" } } }""", Description = "unknown root key")]
	[TestCase("""{ "Sites": { "s": { "RootNodeName": "Corprate site" } } }""", Description = "unknown root name")]
	[TestCase("""{ "Unknown": {} }""", Description = "unknown top-level key, as in the base schema")]
	public void A_typo_is_reported(string rules)
		=> Assert.That(JsonSchemaValidation.Validate(_schema, rules), Is.Not.Empty);

	[Test]
	public void Each_alias_is_described_with_its_name_container_and_origin()
	{
		JsonObject landingPage = Definition("landingPage");

		Assert.Multiple(() =>
		{
			Assert.That((string?)landingPage["description"], Does.StartWith("landingPage (document type, composed of siteSettings)."));
			Assert.That(Values(landingPage, "Properties"), Is.EqualTo(new[] { "bannerImage", "legacyNotes", "metaKeywords", "title" }));
			Assert.That(
				Values(landingPage, "Containers"),
				Is.EqualTo(new[] { "contentTab", "contentTab/main", "legacyTab", "legacyTab/legacy", "seoTab", "seoTab/meta" }));
			Assert.That(Description(landingPage, "Properties", "legacyNotes"), Is.EqualTo("'legacyNotes', in legacyTab/legacy, from composition siteSettings"));
			Assert.That(Description(landingPage, "Properties", "title"), Is.EqualTo("'title', in contentTab/main, mandatory (hiding it blocks publishing)"));
			Assert.That(Description(landingPage, "Containers", "seoTab"), Is.EqualTo("Tab 'Seo tab'"));
			Assert.That(Description(landingPage, "Containers", "seoTab/meta"), Is.EqualTo("Group 'Meta' in tab 'seoTab'"));
			Assert.That(Description(landingPage, "Containers", "legacyTab/legacy"), Is.EqualTo("Group 'Legacy' in tab 'legacyTab', from composition siteSettings"));
			Assert.That(Description(Definition("promoBanner"), "Containers", "main"), Is.EqualTo("Group 'Main' (not in a tab)"));
			Assert.That((string?)Definition("promoBanner")["description"], Does.StartWith("promoBanner (element type)."));
		});
	}

	[Test]
	public void Root_nodes_are_listed_by_key_and_by_name()
	{
		JsonObject site = JsonNode.Parse(_schema)!["definitions"]!["SiteVisibilityOptions"]!["properties"]!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(Strings(site["RootNodeKey"]!["enum"]), Is.EqualTo(new[] { CorporateRoot.ToString(), CampaignRoot.ToString() }));
			Assert.That(Strings(site["RootNodeKey"]!["enumDescriptions"]), Is.EqualTo(new[] { "Corporate site", "Campaign site" }));
			Assert.That(Strings(site["RootNodeName"]!["enum"]), Is.EqualTo(new[] { "Corporate site", "Campaign site" }));
			Assert.That(Strings(site["RootNodeName"]!["enumDescriptions"]), Is.EqualTo(new[] { CorporateRoot.ToString(), CampaignRoot.ToString() }));
		});
	}

	[Test]
	public void Without_root_nodes_the_root_fields_stay_as_in_the_base_schema()
	{
		var schema = AliasSchemaBuilder.Build([_landingPage], []);
		var rules = """{ "Sites": { "s": { "RootNodeKey": "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d", "RootNodeName": "Anything" } } }""";

		Assert.That(JsonSchemaValidation.Validate(schema, rules), Is.Empty);
	}

	[Test]
	public void The_output_depends_only_on_the_input()
	{
		var shuffled = AliasSchemaBuilder.Build([_emptyElement, _siteSettings, _landingPage, _promoBanner], _roots);

		Assert.Multiple(() =>
		{
			Assert.That(shuffled, Is.EqualTo(_schema));
			Assert.That(_schema, Does.EndWith("}\n"));
			Assert.That(_schema, Does.Not.Contain("\r"));
		});
	}

	[Test]
	public void The_embedded_base_schema_is_the_committed_rules_file_schema()
		=> Assert.That(
			AliasSchemaBuilder.Build(RepositoryFiles.Read(BaseSchemaFile), [_promoBanner, _landingPage, _siteSettings, _emptyElement], _roots),
			Is.EqualTo(_schema));

	private JsonObject Definition(string alias) => JsonNode.Parse(_schema)!["definitions"]![$"ContentType.{alias}"]!.AsObject();

	private static string[] Values(JsonObject definition, string list)
		=> Strings(definition["properties"]![list]!["items"]!["enum"]);

	private static string Description(JsonObject definition, string list, string value)
	{
		JsonNode items = definition["properties"]![list]!["items"]!;
		var index = Array.IndexOf(Strings(items["enum"]), value);
		Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{value} is listed");
		return Strings(items["enumDescriptions"])[index];
	}

	private static string[] Strings(JsonNode? array) => array!.AsArray().Select(node => (string)node!).ToArray();
}
