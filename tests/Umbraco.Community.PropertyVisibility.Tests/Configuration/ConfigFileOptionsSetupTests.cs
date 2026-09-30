using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

/// <summary>
///     <see cref="ConfigFileOptionsSetup" /> on a real file in a temporary content root, applied to options as the
///     appsettings binder left them.
/// </summary>
[TestFixture]
public sealed class ConfigFileOptionsSetupTests
{
	private const string FileName = PropertyVisibilityOptions.DefaultConfigFile;
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");

	/// <summary>The file sample of the configuration docs, with a second site.</summary>
	private const string FileSample = """
		{
			"$schema": "./PropertyVisibility.config-schema.json",
			"ContentTypes": {
				"landingPage": { "Containers": ["seoTab"] }
			},
			"Sites": {
				"campaign": {
					"RootNodeName": "Campaign site",
					"ContentTypes": { "article": { "Properties": ["metaKeywords"] } }
				}
			}
		}
		""";

	private TestHostEnvironment _environment = null!;
	private ConfigurationInfo _info = null!;
	private ListLogger<ConfigFileOptionsSetup> _logger = null!;
	private ConfigFileOptionsSetup _setup = null!;

	[SetUp]
	public void SetUp()
	{
		_environment = new TestHostEnvironment();
		_info = new ConfigurationInfo();
		_logger = new ListLogger<ConfigFileOptionsSetup>();
		_setup = new ConfigFileOptionsSetup(_environment, _info, _logger);
	}

	[TearDown]
	public void TearDown() => _environment.Dispose();

	[Test]
	public void File_rules_replace_the_appsettings_rules_wholesale()
	{
		_environment.WriteFile(FileName, FileSample);

		PropertyVisibilityOptions options = Load(AppsettingsWithRules());

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "siteSettings from appsettings is gone, not merged");
			Assert.That(options.ContentTypes["landingPage"].Containers, Is.EqualTo(new[] { "seoTab" }));
			Assert.That(options.ContentTypes["landingPage"].Properties, Is.Empty, "no appsettings properties merged into the same alias");
			Assert.That(options.Sites.Keys, Is.EquivalentTo(new[] { "campaign" }), "the corporate site from appsettings is gone");
			Assert.That(options.Sites["campaign"].RootNodeName, Is.EqualTo("Campaign site"));
			Assert.That(options.Sites["campaign"].ContentTypes["article"].Properties, Is.EqualTo(new[] { "metaKeywords" }));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
			Assert.That(_info.FilePath, Is.EqualTo(Path.Combine(_environment.ContentRootPath, FileName)));
		});
	}

	[Test]
	public void File_dictionaries_are_case_insensitive_at_every_level()
	{
		_environment.WriteFile(FileName, FileSample);

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.ContainsKey("LANDINGPAGE"), Is.True);
			Assert.That(options.Sites.ContainsKey("Campaign"), Is.True);
			Assert.That(options.Sites["CAMPAIGN"].ContentTypes.ContainsKey("Article"), Is.True);
		});
	}

	[TestCase(true, null, true, TestName = "File without HideEmptiedContainers keeps appsettings true")]
	[TestCase(false, null, false, TestName = "File without HideEmptiedContainers keeps appsettings false")]
	[TestCase(true, false, false, TestName = "File false overrides appsettings true")]
	[TestCase(false, true, true, TestName = "File true overrides appsettings false")]
	public void HideEmptiedContainers_is_overridden_only_when_the_file_sets_it(bool appsettings, bool? file, bool expected)
	{
		var setting = file is { } value ? $"\"HideEmptiedContainers\": {(value ? "true" : "false")}," : string.Empty;
		_environment.WriteFile(FileName, $$"""{ {{setting}} "ContentTypes": { "landingPage": { "Properties": ["bannerImage"] } } }""");

		PropertyVisibilityOptions options = Load(new PropertyVisibilityOptions { HideEmptiedContainers = appsettings });

		Assert.That(options.HideEmptiedContainers, Is.EqualTo(expected));
	}

	[TestCase("Enabled", "false")]
	[TestCase("ConfigFile", "\"other.json\"")]
	public void Enabled_and_ConfigFile_cannot_be_set_in_the_file(string key, string value)
	{
		_environment.WriteFile(FileName, $$"""{ "{{key}}": {{value}}, "ContentTypes": { "landingPage": { "Properties": ["bannerImage"] } } }""");

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(options.Enabled, Is.True);
			Assert.That(options.ConfigFile, Is.EqualTo(FileName));
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.UnknownKey));
			Assert.That(issue.Path, Is.EqualTo($"$.{key}"));
			Assert.That(issue.Message, Does.Contain("appsettings").And.Contain($"PropertyVisibility:{key}"));
			Assert.That(options.ContentTypes, Is.Empty, "the file never parsed, so it contributes no rules");
		});
	}

	[Test]
	public void An_absent_file_leaves_the_appsettings_rules_in_effect()
	{
		PropertyVisibilityOptions options = Load(AppsettingsWithRules());

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "siteSettings" }));
			Assert.That(options.Sites.Keys, Is.EquivalentTo(new[] { "corporate" }));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(_info.FilePath, Is.EqualTo(Path.Combine(_environment.ContentRootPath, FileName)), "the path is reported so the health check can name it");
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(_logger.Entries, Is.Empty);
		});
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase(null)]
	public void An_empty_ConfigFile_turns_the_file_source_off(string? configFile)
	{
		_environment.WriteFile(FileName, FileSample);

		PropertyVisibilityOptions options = Load(AppsettingsWithRules(configFile));

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "siteSettings" }), "the file at the default name is ignored");
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(_info.FilePath, Is.Null);
			Assert.That(_info.Issues, Is.Empty);
		});
	}

	[TestCase("", TestName = "Empty file")]
	[TestCase(" \r\n\t\n", TestName = "Whitespace only")]
	[TestCase("// The sample is commented out.\n/* { \"ContentTypes\": { \"landingPage\": { \"Containers\": [\"seoTab\"] } } } */\n", TestName = "Comments only")]
	public void A_file_without_a_JSON_value_counts_as_absent(string content)
	{
		_environment.WriteFile(FileName, content);

		PropertyVisibilityOptions options = Load(AppsettingsWithRules());

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "siteSettings" }));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(_logger.At(LogLevel.Error), Is.Empty);
		});
	}

	[Test]
	public void Invalid_JSON_is_PV001_with_line_and_position()
	{
		var json = string.Join(
			"\n",
			"{",
			"\t\"ContentTypes\": {",
			"\t\t\"landingPage\": { \"Properties\": [\"bannerImage\" \"relatedLinks\"] }",
			"\t}",
			"}");
		_environment.WriteFile(FileName, json);

		Load();

		Assert.Multiple(() =>
		{
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(issue.Message, Does.Contain($"'{FileName}'").And.Contain("at line 3, position 49:"));
			Assert.That(issue.Message, Does.Not.Contain("LineNumber"), "the zero-based System.Text.Json suffix is removed");
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.StartWith("PV001").And.Contain("line 3, position 49"));
		});
	}

	[TestCase("\"caf#\": { \"IsDefault\": true }", 18, TestName = "A file saved as Windows-1252 with a non-ASCII key is PV001 with the line and position of the first invalid byte")]
	[TestCase("\"cafe\": { \"RootNodeName\": \"Caf#\" }", 44, TestName = "A file saved as Windows-1252 with a non-ASCII value is PV001 with the line and position of the first invalid byte")]
	public void A_file_that_is_not_UTF8_is_PV001_advising_to_save_it_as_UTF8(string site, int position)
	{
		// '#' stands for U+00E9 (e acute), which Windows-1252 and Latin-1 store as the single byte 0xE9.
		var json = $"{{\n  \"Sites\": {{ {site.Replace('#', (char)0xE9)} }}\n}}";
		File.WriteAllBytes(Path.Combine(_environment.ContentRootPath, FileName), Encoding.Latin1.GetBytes(json));

		Load();

		Assert.Multiple(() =>
		{
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Message, Does.Contain($"Rules file '{FileName}' is not valid UTF-8 at line 2, position {position}"));
			Assert.That(issue.Message, Does.Contain("Save the file as UTF-8."));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.None), "no rules without a valid version");
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_file_cut_off_inside_a_UTF8_sequence_is_PV001_advising_to_save_it_as_UTF8()
	{
		byte[] content = [.. Encoding.UTF8.GetBytes("{ \"Sites\": {} }"), 0xC3];
		File.WriteAllBytes(Path.Combine(_environment.ContentRootPath, FileName), content);

		Load();

		Assert.That(_info.Issues.Single().Message, Does.Contain("is not valid UTF-8 at line 1, position 16"));
	}

	[TestCase(
		"""{ "ContentTypes": { "landingPage": { "Propertes": ["bannerImage"] } } }""",
		"$.ContentTypes.landingPage.Propertes",
		"Properties",
		TestName = "Propertes suggests Properties")]
	[TestCase(
		"""{ "Sites": { "corporate": { "RootNodeNme": "Corporate site" } } }""",
		"$.Sites.corporate.RootNodeNme",
		"RootNodeName",
		TestName = "RootNodeNme suggests RootNodeName")]
	[TestCase(
		"""{ "HideEmptyContainers": false }""",
		"$.HideEmptyContainers",
		"HideEmptiedContainers",
		TestName = "HideEmptyContainers suggests HideEmptiedContainers")]
	[TestCase(
		"""{ "Sites": { "my site": { "IsDefault": true, "ContentType": {} } } }""",
		"$.Sites['my site'].ContentType",
		"ContentTypes",
		TestName = "A label with a space is written in bracket notation")]
	[TestCase(
		"""{ "RuleSets": { "simplePages": { "ContentType": {} } } }""",
		"$.RuleSets.simplePages.ContentType",
		"ContentTypes",
		TestName = "ContentType in a rule set suggests ContentTypes")]
	[TestCase(
		"""{ "Sites": { "corporate": { "IsDefault": true, "Includes": ["simplePages"] } } }""",
		"$.Sites.corporate.Includes",
		"Include",
		TestName = "Includes suggests Include")]
	public void An_unknown_key_is_PV002_with_its_path_and_a_suggestion(string json, string path, string suggestion)
	{
		_environment.WriteFile(FileName, json);

		Load();

		Assert.Multiple(() =>
		{
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.UnknownKey));
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Error));
			Assert.That(issue.Path, Is.EqualTo(path));
			Assert.That(issue.Suggestion, Is.EqualTo(suggestion));
			Assert.That(issue.ToString(), Does.EndWith($"Did you mean '{suggestion}'?"));
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.Contain(path).And.Contain($"Did you mean '{suggestion}'?"));
		});
	}

	[Test]
	public void Every_unknown_key_is_reported_and_a_far_one_gets_no_suggestion()
	{
		_environment.WriteFile(
			FileName,
			"""{ "Colour": "red", "ContentTypes": { "landingPage": { "Propertes": [], "Containerz": [] } }, "Sites": { "corporate": { "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b", "Unrelated": 1 } } }""");

		Load();

		Assert.That(
			_info.Issues.Select(issue => (issue.Code, issue.Path, issue.Suggestion)),
			Is.EqualTo(new (string, string?, string?)[]
			{
				(IssueCodes.UnknownKey, "$.Colour", null),
				(IssueCodes.UnknownKey, "$.ContentTypes.landingPage.Propertes", "Properties"),
				(IssueCodes.UnknownKey, "$.ContentTypes.landingPage.Containerz", "Containers"),
				(IssueCodes.UnknownKey, "$.Sites.corporate.Unrelated", null),
			}));
		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1), "one error per change, listing every issue");
	}

	[Test]
	public void A_value_of_the_wrong_type_is_PV001_with_its_path()
	{
		_environment.WriteFile(FileName, """{ "Sites": { "corporate": { "RootNodeKey": "not-a-guid" } } }""");

		Load();

		ConfigurationIssue issue = _info.Issues.Single();
		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Path, Is.EqualTo("$.Sites.corporate.RootNodeKey"));
			Assert.That(issue.Message, Does.Contain("at line 1, position"));
		});
	}

	[TestCase("""{ "ContentTypes": { "landingPage": {}, "LandingPage": {} } }""", "$.ContentTypes.LandingPage", TestName = "Content type alias twice, differing in case")]
	[TestCase("""{ "Sites": { "a": { "IsDefault": true, "isDefault": false } } }""", "$.Sites.a.isDefault", TestName = "Model key twice, differing in case")]
	public void A_key_given_twice_is_PV001(string json, string path)
	{
		_environment.WriteFile(FileName, json);

		Load();

		ConfigurationIssue issue = _info.Issues.Single();
		Assert.Multiple(() =>
		{
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Path, Is.EqualTo(path));
			Assert.That(issue.Message, Does.Contain("more than once"));
		});
	}

	[TestCase("[]")]
	[TestCase("\"rules\"")]
	[TestCase("null")]
	public void A_root_that_is_not_an_object_is_PV001(string json)
	{
		_environment.WriteFile(FileName, json);

		Load();

		Assert.That(_info.Issues.Single().Code, Is.EqualTo(IssueCodes.InvalidJson));
	}

	[Test]
	public void The_last_good_parse_stays_in_effect_after_an_invalid_edit()
	{
		_environment.WriteFile(FileName, FileSample);
		Assert.That(Load().ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));
		var goodHash = _info.RulesHash;

		_environment.WriteFile(FileName, """{ "ContentTypes": { "article": { "Properties": ["metaKeywords"] } """);
		PropertyVisibilityOptions afterInvalidEdit = Load();

		Assert.Multiple(() =>
		{
			Assert.That(afterInvalidEdit.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "the last good rules");
			Assert.That(afterInvalidEdit.Sites.Keys, Is.EquivalentTo(new[] { "campaign" }));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
			Assert.That(_info.RulesHash, Is.EqualTo(goodHash));
			Assert.That(_info.Issues.Single().Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.Contain("last valid version"));
		});

		Load();
		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1), "a rebuild with the same invalid content logs nothing new");

		_environment.WriteFile(FileName, """{ "ContentTypes": { "article": { "Propertes": [] } } }""");
		Assert.That(Load().ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "still the last good rules");
		Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(2), "a different invalid content is logged once");

		_environment.WriteFile(FileName, """{ "ContentTypes": { "article": { "Properties": ["metaKeywords"] } } }""");
		PropertyVisibilityOptions fixedOptions = Load();

		Assert.Multiple(() =>
		{
			Assert.That(fixedOptions.ContentTypes.Keys, Is.EquivalentTo(new[] { "article" }));
			Assert.That(fixedOptions.Sites, Is.Empty);
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(_info.RulesHash, Is.Not.EqualTo(goodHash));
		});
	}

	[Test]
	public void An_invalid_file_that_never_parsed_contributes_empty_rules()
	{
		_environment.WriteFile(FileName, "{ \"ContentTypes\": ");

		PropertyVisibilityOptions options = Load(AppsettingsWithRules());

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes, Is.Empty, "the appsettings rules are replaced, not restored");
			Assert.That(options.Sites, Is.Empty);
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.None));
			Assert.That(_info.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.InvalidJson, IssueCodes.BothSourcesDefineRules }));
			Assert.That(_logger.At(LogLevel.Error)[0].Message, Does.Contain("contributes no rules"));
		});
	}

	[Test]
	public void Rules_in_both_sources_are_PV301_and_the_file_wins()
	{
		_environment.WriteFile(FileName, FileSample);

		PropertyVisibilityOptions options = Load(AppsettingsWithRules());
		Load(AppsettingsWithRules());

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.BothSourcesDefineRules));
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(_logger.At(LogLevel.Warning), Has.Count.EqualTo(1), "logged once, not per rebuild");
			Assert.That(_logger.At(LogLevel.Warning)[0].Message, Does.StartWith(IssueCodes.BothSourcesDefineRules));
		});
	}

	[Test]
	public void File_rule_sets_and_includes_replace_the_appsettings_ones()
	{
		_environment.WriteFile(
			FileName,
			"""
			{
				"RuleSets": { "simplePages": { "ContentTypes": { "landingPage": { "Containers": ["seoTab"] }, "article": null } } },
				"Sites": { "campaign": { "RootNodeName": "Campaign site", "Include": ["simplePages"] } }
			}
			""");
		PropertyVisibilityOptions appsettings = AppsettingsWithRules();
		appsettings.RuleSets["fromAppsettings"] = new RuleSetOptions();

		PropertyVisibilityOptions options = Load(appsettings);

		Assert.Multiple(() =>
		{
			Assert.That(options.RuleSets.Keys, Is.EquivalentTo(new[] { "simplePages" }), "the appsettings rule set is gone, not merged");
			Assert.That(options.RuleSets["SIMPLEPAGES"].ContentTypes["LandingPage"].Containers, Is.EqualTo(new[] { "seoTab" }));
			Assert.That(options.RuleSets["simplePages"].ContentTypes["article"].Properties, Is.Empty, "a null entry becomes an empty one");
			Assert.That(options.Sites["campaign"].Include, Is.EqualTo(new[] { "simplePages" }));
			Assert.That(_info.Issues.Single().Code, Is.EqualTo(IssueCodes.BothSourcesDefineRules));
		});
	}

	[Test]
	public void A_null_include_in_the_file_is_dropped()
	{
		// As a null alias in Properties and Containers. Appsettings keeps it, and the validator reports it (PV009).
		_environment.WriteFile(
			FileName,
			"""{ "RuleSets": { "simplePages": {} }, "Sites": { "campaign": { "RootNodeName": "Campaign site", "Include": [null, "simplePages"] } } }""");

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(options.Sites["campaign"].Include, Is.EqualTo(new[] { "simplePages" }));
			Assert.That(_info.Issues, Is.Empty);
		});
	}

	[Test]
	public void Rule_sets_alone_in_appsettings_next_to_the_file_are_PV301()
	{
		_environment.WriteFile(FileName, FileSample);
		var appsettings = new PropertyVisibilityOptions();
		appsettings.RuleSets["simplePages"] = new RuleSetOptions();

		PropertyVisibilityOptions options = Load(appsettings);

		Assert.Multiple(() =>
		{
			Assert.That(options.RuleSets, Is.Empty, "the file has no rule sets and replaces the appsettings ones");
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.BothSourcesDefineRules));
			Assert.That(issue.Message, Does.Contain("(PropertyVisibility:ContentTypes / RuleSets / Sites)"));
		});
	}

	[Test]
	public void Appsettings_without_rules_next_to_the_file_is_not_PV301()
	{
		_environment.WriteFile(FileName, FileSample);

		Load(new PropertyVisibilityOptions { HideEmptiedContainers = false });

		Assert.Multiple(() =>
		{
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(_logger.At(LogLevel.Warning), Is.Empty);
		});
	}

	[Test]
	public void CamelCase_keys_are_accepted()
	{
		_environment.WriteFile(
			FileName,
			"""
			{
				"$schema": "./PropertyVisibility.config-schema.json",
				"hideEmptiedContainers": false,
				"contentTypes": { "landingPage": { "properties": ["bannerImage"], "containers": ["seoTab"] } },
				"sites": {
					"corporate": { "rootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b", "rootNodeName": "Corporate site", "isDefault": true,
						"contentTypes": { "article": { "containers": ["shareTab"] } } }
				}
			}
			""");

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(options.HideEmptiedContainers, Is.False);
			Assert.That(options.ContentTypes["landingPage"].Properties, Is.EqualTo(new[] { "bannerImage" }));
			Assert.That(options.ContentTypes["landingPage"].Containers, Is.EqualTo(new[] { "seoTab" }));
			Assert.That(options.Sites["corporate"].RootNodeKey, Is.EqualTo(CorporateRoot));
			Assert.That(options.Sites["corporate"].RootNodeName, Is.EqualTo("Corporate site"));
			Assert.That(options.Sites["corporate"].IsDefault, Is.True);
			Assert.That(options.Sites["corporate"].ContentTypes["article"].Containers, Is.EqualTo(new[] { "shareTab" }));
		});
	}

	[Test]
	public void Comments_trailing_commas_and_a_byte_order_mark_are_accepted()
	{
		const string json = """
			{
				// Global rules
				"ContentTypes": {
					/* the landing page */
					"landingPage": { "Properties": ["bannerImage", "relatedLinks",], },
				},
			}
			""";
		File.WriteAllBytes(Path.Combine(_environment.ContentRootPath, FileName), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(options.ContentTypes["landingPage"].Properties, Is.EqualTo(new[] { "bannerImage", "relatedLinks" }));
		});
	}

	[TestCase("utf-16", TestName = "UTF-16 little endian (Windows PowerShell 5.1 Out-File)")]
	[TestCase("utf-16BE", TestName = "UTF-16 big endian")]
	[TestCase("utf-32", TestName = "UTF-32 little endian")]
	[TestCase("utf-32BE", TestName = "UTF-32 big endian")]
	public void A_file_with_a_UTF_16_or_UTF_32_byte_order_mark_is_transcoded(string encodingName)
	{
		Encoding encoding = encodingName == "utf-32BE" ? new UTF32Encoding(bigEndian: true, byteOrderMark: true) : Encoding.GetEncoding(encodingName);
		File.WriteAllText(Path.Combine(_environment.ContentRootPath, FileName), FileSample, encoding);
		Assert.That(File.ReadAllBytes(Path.Combine(_environment.ContentRootPath, FileName))[0], Is.AnyOf((byte)0xFF, (byte)0xFE, (byte)0x00), "precondition: written with a byte order mark");

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(options.ContentTypes["landingPage"].Containers, Is.EqualTo(new[] { "seoTab" }));
			Assert.That(options.Sites["campaign"].RootNodeName, Is.EqualTo("Campaign site"));
		});
	}

	[Test]
	public void A_file_larger_than_1_MiB_is_PV001_is_not_read_and_keeps_the_last_valid_version()
	{
		_environment.WriteFile(FileName, FileSample);
		Assert.That(Load().ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));

		var padding = new string(' ', (int)ConfigFileOptionsSetup.MaxFileSizeBytes);
		_environment.WriteFile(FileName, $$"""{ "ContentTypes": { "article": { "Properties": ["metaKeywords"] } } }{{padding}}""");
		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "the last valid version");
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Message, Does.Contain("larger than the limit of 1 MiB"));
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_locked_file_is_PV001_and_is_read_again_with_a_backoff_until_it_can_be_read()
	{
		var retry = new ConfigFileReadRetry();
		var scheduled = new List<TimeSpan>();
		retry.Attach(scheduled.Add);
		var setup = new ConfigFileOptionsSetup(_environment, _info, retry, _logger);
		var path = _environment.WriteFile(FileName, FileSample);
		Assert.That(Load(setup).ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));

		using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			locked.SetLength(0);
			locked.Write(Encoding.UTF8.GetBytes("""{ "ContentTypes": { "article": { "Properties": ["metaKeywords"] } } }"""));
			locked.Flush(flushToDisk: true);

			for (var read = 0; read < 5; read++)
			{
				Assert.That(Load(setup).ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "the last valid version while the file is locked");
			}

			Assert.Multiple(() =>
			{
				ConfigurationIssue issue = _info.Issues.Single();
				Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
				Assert.That(issue.Message, Does.Contain("cannot be read").And.Contain("read again automatically"));
				Assert.That(scheduled, Is.EqualTo(new[] { 1, 2, 5, 30, 30 }.Select(seconds => TimeSpan.FromSeconds(seconds))), "1, 2 and 5 seconds, then every 30 seconds");
				Assert.That(retry.ConsecutiveFailures, Is.EqualTo(5));
				Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1), "the same failure is logged once");
			});
		}

		PropertyVisibilityOptions released = Load(setup);

		Assert.Multiple(() =>
		{
			Assert.That(released.ContentTypes.Keys, Is.EquivalentTo(new[] { "article" }), "the edit made under the lock");
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(retry.ConsecutiveFailures, Is.Zero, "a successful read resets the backoff");
			Assert.That(scheduled, Has.Count.EqualTo(5), "no retry after a successful read");
		});
	}

	[Test]
	public void A_file_the_site_may_not_read_is_PV001_and_is_not_retried_until_the_next_change()
	{
		var retry = new ConfigFileReadRetry();
		var scheduled = new List<TimeSpan>();
		retry.Attach(scheduled.Add);
		var setup = new ConfigFileOptionsSetup(_environment, _info, retry, _logger);
		var path = _environment.WriteFile(FileName, FileSample);
		Assert.That(Load(setup).ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));

		_environment.WriteFile(FileName, """{ "ContentTypes": { "article": { "Properties": ["metaKeywords"] } } }""");
		using (DenyRead(path))
		{
			for (var read = 0; read < 3; read++)
			{
				Assert.That(Load(setup).ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }), "the last valid version while the file cannot be read");
			}

			Assert.Multiple(() =>
			{
				ConfigurationIssue issue = _info.Issues.Single();
				Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
				Assert.That(issue.Message, Does.Contain("cannot be read").And.Contain("not read again until the file or appsettings changes"));
				Assert.That(scheduled, Is.Empty, "access denied does not go away by itself: no retry");
				Assert.That(retry.ConsecutiveFailures, Is.Zero);
				Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(1), "the same failure is logged once");
				Assert.That(_logger.At(LogLevel.Error)[0].Exception, Is.InstanceOf<UnauthorizedAccessException>());
			});
		}

		// The next change after the permissions are fixed reads the file.
		Assert.Multiple(() =>
		{
			Assert.That(Load(setup).ContentTypes.Keys, Is.EquivalentTo(new[] { "article" }));
			Assert.That(_info.Issues, Is.Empty);
		});
	}

	[TestCase(typeof(IOException), true, TestName = "A sharing violation or another I/O error is transient")]
	[TestCase(typeof(UnauthorizedAccessException), false, TestName = "Denied access is not transient")]
	[TestCase(typeof(PathTooLongException), false, TestName = "A path that is too long is not transient")]
	[TestCase(typeof(FileNotFoundException), false, TestName = "A missing file is not a read failure to retry")]
	public void Only_a_failure_that_can_go_away_by_itself_is_retried(Type exceptionType, bool transient)
		=> Assert.That(ConfigFileOptionsSetup.IsTransientReadFailure((Exception)Activator.CreateInstance(exceptionType)!), Is.EqualTo(transient));

	[Test]
	public void A_device_that_reports_no_length_is_read_up_to_the_size_limit_only()
	{
		// /dev/zero reports a length of 0 and never ends; the read must stop at the limit instead of filling memory.
		const string endless = "/dev/zero";
		if (!File.Exists(endless))
		{
			Assert.Ignore($"{endless} exists on Linux and macOS only.");
		}

		PropertyVisibilityOptions options = Load(new PropertyVisibilityOptions { ConfigFile = endless });

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes, Is.Empty);
			ConfigurationIssue issue = _info.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(issue.Message, Does.Contain("is more than 1 MiB, larger than the limit of 1 MiB"));
		});
	}

	[Test]
	public void A_version_that_fails_validation_never_becomes_the_last_valid_version()
	{
		_environment.WriteFile(FileName, FileSample);
		Assert.That(Load().ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));

		// Parses, but a site without identity (PV003): applied, and the options monitor then fails open (PV008).
		_environment.WriteFile(FileName, """{ "Sites": { "noIdentity": { "ContentTypes": { "article": { "Properties": ["metaKeywords"] } } } } }""");
		Assert.That(Load().Sites.Keys, Is.EquivalentTo(new[] { "noIdentity" }));

		// A syntax error next: the last valid version is the one before the invalid one.
		_environment.WriteFile(FileName, """{ "ContentTypes": """);
		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));
			Assert.That(options.Sites.Keys, Is.EquivalentTo(new[] { "campaign" }));
			Assert.That(new PropertyVisibilityOptionsValidator().Validate(null, options).Succeeded, Is.True, "what stays in effect is valid");
			Assert.That(_logger.At(LogLevel.Error).Single().Message, Does.Contain("the last valid version of the file stays in effect"));
		});
	}

	[Test]
	public void A_load_that_fails_validation_keeps_the_load_time_and_hash_of_the_last_successful_load()
	{
		_environment.WriteFile(FileName, FileSample);
		Load();
		DateTimeOffset? loadedAt = _info.LoadedAt;
		var rulesHash = _info.RulesHash;
		Assert.That(loadedAt, Is.Not.Null);

		_environment.WriteFile(FileName, """{ "Sites": { "a": { "IsDefault": true }, "b": { "IsDefault": true } } }""");
		Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.LoadedAt, Is.EqualTo(loadedAt));
			Assert.That(_info.RulesHash, Is.EqualTo(rulesHash));
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.File), "source, path and issues describe the latest load");
		});
	}

	[Test]
	public void Options_that_never_passed_validation_have_no_load_time_or_hash()
	{
		var options = new PropertyVisibilityOptions();
		options.Sites["a"] = new SiteVisibilityOptions { IsDefault = true };
		options.Sites["b"] = new SiteVisibilityOptions { IsDefault = true };

		Load(options);

		Assert.Multiple(() =>
		{
			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(_info.LoadedAt, Is.Null);
			Assert.That(_info.RulesHash, Is.Null);
		});
	}

	[Test]
	public void Null_collections_and_entries_become_empty()
	{
		_environment.WriteFile(
			FileName,
			"""{ "ContentTypes": { "landingPage": null, "article": { "Properties": null, "Containers": ["shareTab", null] } }, "Sites": { "corporate": { "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b", "ContentTypes": null } } }""");

		PropertyVisibilityOptions options = Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.Issues, Is.Empty);
			Assert.That(options.ContentTypes["landingPage"].Properties, Is.Empty);
			Assert.That(options.ContentTypes["article"].Properties, Is.Empty);
			Assert.That(options.ContentTypes["article"].Containers, Is.EqualTo(new[] { "shareTab" }));
			Assert.That(options.Sites["corporate"].ContentTypes, Is.Empty);
		});
	}

	[Test]
	public void A_file_outside_the_content_root_is_loaded_from_its_absolute_path()
	{
		var elsewhere = TemporaryFolder.Create();
		try
		{
			var path = Path.Combine(elsewhere, "rules.json");
			File.WriteAllText(path, FileSample);

			PropertyVisibilityOptions options = Load(new PropertyVisibilityOptions { ConfigFile = path });

			Assert.Multiple(() =>
			{
				Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));
				Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
				Assert.That(_info.FilePath, Is.EqualTo(path));
			});
		}
		finally
		{
			TemporaryFolder.Delete(elsewhere);
		}
	}

	[Test]
	public void Each_options_instance_gets_its_own_copy_of_the_rules()
	{
		_environment.WriteFile(FileName, FileSample);

		PropertyVisibilityOptions first = Load();
		first.ContentTypes["landingPage"].Containers.Add("mutated");
		first.Sites.Clear();
		PropertyVisibilityOptions second = Load();

		Assert.Multiple(() =>
		{
			Assert.That(second.ContentTypes["landingPage"].Containers, Is.EqualTo(new[] { "seoTab" }));
			Assert.That(second.Sites.Keys, Is.EquivalentTo(new[] { "campaign" }));
		});
	}

	[Test]
	public void The_rules_hash_ignores_formatting_and_order_but_not_rule_changes()
	{
		_environment.WriteFile(FileName, """{ "ContentTypes": { "landingPage": { "Properties": ["a", "b"] }, "article": { "Containers": ["shareTab"] } } }""");
		Load();
		var original = _info.RulesHash;

		_environment.WriteFile(
			FileName,
			"""
			{
				// reformatted, reordered, commented
				"ContentTypes": {
					"article":     { "Containers": [ "shareTab" ] },
					"landingPage": { "Properties": [ "b", "a" ] },
				}
			}
			""");
		Load();
		var reformatted = _info.RulesHash;

		_environment.WriteFile(FileName, """{ "ContentTypes": { "landingPage": { "Properties": ["a", "c"] }, "article": { "Containers": ["shareTab"] } } }""");
		Load();
		var changed = _info.RulesHash;

		Assert.Multiple(() =>
		{
			Assert.That(original, Does.Match("^[0-9a-f]{64}$"));
			Assert.That(reformatted, Is.EqualTo(original), "whitespace, comments and order are not rule changes");
			Assert.That(changed, Is.Not.EqualTo(original));
		});
	}

	[Test]
	public void Rules_without_rule_sets_hash_as_they_did_before_rule_sets_existed()
	{
		// The canonical JSON the hash was computed from before rule sets existed. Rules without rule sets and without
		// Include must still produce it, so an upgrade does not change the hash the health check shows.
		const string canonical = """{"HideEmptiedContainers":true,"ContentTypes":{"siteSettings":{"Properties":[],"Containers":["legacyTab"]}},"Sites":{"corporate":{"RootNodeKey":"5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b","RootNodeName":"Corporate site","IsDefault":false,"ContentTypes":{"landingPage":{"Properties":["bannerImage"],"Containers":[]}}}}}""";

		Assert.That(RulesHash.Compute(AppsettingsWithRules()), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))));
	}

	[Test]
	public void The_rules_hash_covers_rule_sets_and_includes_but_not_the_order_of_an_include()
	{
		PropertyVisibilityOptions options = AppsettingsWithRules();
		var withoutRuleSets = RulesHash.Compute(options);

		options.RuleSets["simplePages"] = new RuleSetOptions { ContentTypes = { ["landingPage"] = new ContentTypeVisibilityOptions { Containers = ["seoTab"] } } };
		options.RuleSets["tracking"] = new RuleSetOptions();
		var withRuleSets = RulesHash.Compute(options);

		options.Sites["corporate"].Include = ["simplePages", "tracking"];
		var withInclude = RulesHash.Compute(options);

		options.Sites["corporate"].Include = ["tracking", "simplePages"];
		var reordered = RulesHash.Compute(options);

		options.RuleSets["simplePages"].ContentTypes["landingPage"].Containers = ["shareTab"];
		var changedRule = RulesHash.Compute(options);

		Assert.Multiple(() =>
		{
			Assert.That(withRuleSets, Is.Not.EqualTo(withoutRuleSets));
			Assert.That(withInclude, Is.Not.EqualTo(withRuleSets));
			Assert.That(reordered, Is.EqualTo(withInclude), "the order of Include is not a rule change");
			Assert.That(changedRule, Is.Not.EqualTo(withInclude));
		});
	}

	[Test]
	public void The_same_rules_hash_the_same_from_either_source()
	{
		PropertyVisibilityOptions appsettings = AppsettingsWithRules();
		Load(appsettings);
		var fromAppsettings = _info.RulesHash;

		_environment.WriteFile(
			FileName,
			"""
			{
				"ContentTypes": { "siteSettings": { "Containers": ["legacyTab"] } },
				"Sites": { "corporate": { "RootNodeKey": "5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b", "RootNodeName": "Corporate site",
					"ContentTypes": { "landingPage": { "Properties": ["bannerImage"] } } } }
			}
			""");
		Load();

		Assert.That(_info.RulesHash, Is.EqualTo(fromAppsettings));
	}

	[Test]
	public void Configuration_info_records_the_load_time_in_utc()
	{
		DateTimeOffset before = DateTimeOffset.UtcNow;
		Assert.That(_info.Current, Is.SameAs(ConfigurationState.NotLoaded));

		Load();

		Assert.Multiple(() =>
		{
			Assert.That(_info.LoadedAt, Is.Not.Null);
			Assert.That(_info.LoadedAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
			Assert.That(_info.LoadedAt.Value, Is.InRange(before, DateTimeOffset.UtcNow));
		});
	}

	[Test]
	public void Concurrent_rebuilds_are_safe_and_agree()
	{
		_environment.WriteFile(FileName, FileSample);
		var results = new PropertyVisibilityOptions[64];

		Parallel.For(0, results.Length, index => results[index] = Load(AppsettingsWithRules()));

		Assert.Multiple(() =>
		{
			foreach (PropertyVisibilityOptions options in results)
			{
				Assert.That(options.ContentTypes.Keys, Is.EquivalentTo(new[] { "landingPage" }));
				Assert.That(options.Sites.Keys, Is.EquivalentTo(new[] { "campaign" }));
			}

			Assert.That(_info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
			Assert.That(_info.Issues.Single().Code, Is.EqualTo(IssueCodes.BothSourcesDefineRules));
			Assert.That(_logger.At(LogLevel.Warning), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Information), Has.Count.EqualTo(1));
		});
	}

	private static PropertyVisibilityOptions AppsettingsWithRules(string? configFile = FileName)
	{
		var options = new PropertyVisibilityOptions { ConfigFile = configFile };
		options.ContentTypes["siteSettings"] = new ContentTypeVisibilityOptions { Containers = ["legacyTab"] };
		var corporate = new SiteVisibilityOptions { RootNodeKey = CorporateRoot, RootNodeName = "Corporate site" };
		corporate.ContentTypes["landingPage"] = new ContentTypeVisibilityOptions { Properties = ["bannerImage"] };
		options.Sites["corporate"] = corporate;
		return options;
	}

	// Takes read access to the file away from this process until disposed: a deny entry for the current user on Windows,
	// mode 000 elsewhere (ignored as root, which reads any file).
	private static IDisposable DenyRead(string path)
	{
		return OperatingSystem.IsWindows() ? DenyReadOnWindows(path) : DenyReadOnUnix(path);
	}

	[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
	private static IDisposable DenyReadOnUnix(string path)
	{
		UnixFileMode mode = File.GetUnixFileMode(path);
		File.SetUnixFileMode(path, UnixFileMode.None);
		var restore = new Restore(() => File.SetUnixFileMode(path, mode));
		try
		{
			using (File.OpenRead(path))
			{
			}

			restore.Dispose();
			Assert.Ignore("This process can read a file with mode 000 (running as root), so access cannot be denied.");
		}
		catch (UnauthorizedAccessException)
		{
			// Denied, as intended.
		}

		return restore;
	}

	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static IDisposable DenyReadOnWindows(string path)
	{
		var file = new FileInfo(path);
		var rule = new System.Security.AccessControl.FileSystemAccessRule(
			System.Security.Principal.WindowsIdentity.GetCurrent().User!,
			System.Security.AccessControl.FileSystemRights.ReadData,
			System.Security.AccessControl.AccessControlType.Deny);
		System.Security.AccessControl.FileSecurity security = file.GetAccessControl();
		security.AddAccessRule(rule);
		file.SetAccessControl(security);
		return new Restore(() =>
		{
			System.Security.AccessControl.FileSecurity current = file.GetAccessControl();
			current.RemoveAccessRule(rule);
			file.SetAccessControl(current);
		});
	}

	private PropertyVisibilityOptions Load(PropertyVisibilityOptions? options = null) => Load(_setup, options);

	private static PropertyVisibilityOptions Load(ConfigFileOptionsSetup setup, PropertyVisibilityOptions? options = null)
	{
		options ??= new PropertyVisibilityOptions();
		setup.Configure(options);
		return options;
	}

	private sealed class Restore(Action restore) : IDisposable
	{
		private Action? _restore = restore;

		public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
	}
}
