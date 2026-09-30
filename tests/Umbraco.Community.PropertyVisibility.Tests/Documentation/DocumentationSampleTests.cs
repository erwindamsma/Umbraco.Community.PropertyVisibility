using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Documentation;

/// <summary>
///     The configuration samples users copy from the READMEs and docs/configuration.md are valid: each complete JSON sample
///     validates against the package's JSON schema and loads without issues. The tagline is the same everywhere it is shown.
/// </summary>
[TestFixture]
public sealed class DocumentationSampleTests
{
	private const string AppSettingsSchemaFile = "src/Umbraco.Community.PropertyVisibility/appsettings-schema.Umbraco.Community.PropertyVisibility.json";
	private const string ConfigFileSchemaFile = "src/Umbraco.Community.PropertyVisibility/PropertyVisibility.config-schema.json";
	private const string PackageProject = "src/Umbraco.Community.PropertyVisibility/Umbraco.Community.PropertyVisibility.csproj";

	private static readonly string[] RulesFileKeys = ["$schema", "HideEmptiedContainers", "ContentTypes", "RuleSets", "Sites"];

	private static readonly JsonDocumentOptions JsonWithComments = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>The files with configuration samples, and how many complete samples each holds at least.</summary>
	private static IEnumerable<TestCaseData> FilesWithSamples()
	{
		yield return new TestCaseData("README.md", 2);
		yield return new TestCaseData("docs/README_nuget.md", 1);
		yield return new TestCaseData("umbraco-marketplace-readme.md", 1);
		yield return new TestCaseData("docs/configuration.md", 3);
	}

	[TestCaseSource(nameof(FilesWithSamples))]
	public void Complete_JSON_samples_validate_against_the_package_schemas(string file, int expectedSamples)
	{
		var checkedSamples = 0;
		var problems = new List<string>();
		foreach (var block in MarkdownDocument.Load(file).CodeBlocks.Where(IsJson))
		{
			// Fragments ("corporate": { ... }) show part of a larger document; only whole documents are checked.
			if (!block.Content.TrimStart().StartsWith('{'))
			{
				continue;
			}

			JsonObject sample;
			try
			{
				sample = JsonNode.Parse(block.Content, documentOptions: JsonWithComments)!.AsObject();
			}
			catch (JsonException exception)
			{
				problems.Add($"{file}:{block.Line}: not valid JSON: {exception.Message}");
				continue;
			}

			string? schemaFile = sample.ContainsKey("PropertyVisibility") ? AppSettingsSchemaFile
				: sample.Any(property => RulesFileKeys.Contains(property.Key, StringComparer.Ordinal)) ? ConfigFileSchemaFile
				: null;
			if (schemaFile is null)
			{
				continue;
			}

			checkedSamples++;
			IReadOnlyList<SchemaError> errors = Evaluate(schemaFile, block.Content);
			if (errors.Count > 0)
			{
				problems.Add($"{file}:{block.Line}: does not match {Path.GetFileName(schemaFile)}: {Describe(errors)}");
			}
		}

		Assert.Multiple(() =>
		{
			Assert.That(problems, Is.Empty);
			Assert.That(checkedSamples, Is.GreaterThanOrEqualTo(expectedSamples), "complete configuration samples found");
		});
	}

	[TestCaseSource(nameof(FilesWithSamples))]
	public void Complete_JSON_samples_load_without_issues(string file, int expectedSamples)
	{
		var loaded = 0;
		foreach (var block in MarkdownDocument.Load(file).CodeBlocks.Where(IsJson).Where(block => block.Content.TrimStart().StartsWith('{')))
		{
			var sample = JsonNode.Parse(block.Content, documentOptions: JsonWithComments)!.AsObject();
			if (sample.ContainsKey("PropertyVisibility"))
			{
				// Bound and validated the way the site does it: an unknown key or a failed validation throws here.
				using var configuration = new JsonConfiguration(block.Content);
				Assert.DoesNotThrow(() => _ = configuration.Monitor.CurrentValue, $"{file}:{block.Line}");
				loaded++;
			}
			else if (sample.Any(property => RulesFileKeys.Contains(property.Key, StringComparer.Ordinal)))
			{
				var parsed = ConfigFileParser.Parse(Encoding.UTF8.GetBytes(block.Content), $"{file}:{block.Line}");
				Assert.That(parsed.Issues.Select(issue => $"{issue.Code}: {issue.Message}"), Is.Empty, $"{file}:{block.Line}");
				Assert.That(parsed.Rules, Is.Not.Null, $"{file}:{block.Line}");
				loaded++;
			}
		}

		Assert.That(loaded, Is.GreaterThanOrEqualTo(expectedSamples), "complete configuration samples found");
	}

	[Test]
	public void The_tagline_is_the_same_wherever_it_is_shown()
	{
		var tagline = XDocument.Parse(RepositoryFiles.Read(PackageProject)).Descendants("Description").Single().Value;
		var marketplace = JsonNode.Parse(RepositoryFiles.Read("umbraco-marketplace.json"))!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(tagline, Does.Not.EndWith("."), "the package Description is a tagline without a full stop");
			foreach (var file in new[] { "README.md", "docs/README_nuget.md", "umbraco-marketplace-readme.md" })
			{
				Assert.That(Lines(file), Does.Contain(tagline + "."), $"{file} states the tagline on a line of its own");
			}

			Assert.That(marketplace["Title"]!.GetValue<string>(), Is.EqualTo(XDocument.Parse(RepositoryFiles.Read(PackageProject)).Descendants("Title").Single().Value));
			Assert.That(marketplace["Description"]!.GetValue<string>(), Does.StartWith(tagline + ". "), "the Marketplace description starts with the tagline");
		});
	}

	private static bool IsJson(MarkdownCodeBlock block) =>
		block.Info.Equals("json", StringComparison.OrdinalIgnoreCase) || block.Info.Equals("jsonc", StringComparison.OrdinalIgnoreCase);

	private static string[] Lines(string file) => RepositoryFiles.Read(file).ReplaceLineEndings("\n").Split('\n');

	/// <summary>
	///     Evaluates a sample against a committed schema (draft-04, like Umbraco's own; see JsonSchemaTests).
	/// </summary>
	private static IReadOnlyList<SchemaError> Evaluate(string schemaFile, string json)
		=> JsonSchemaValidation.Validate(RepositoryFiles.Read(schemaFile), json);

	private static string Describe(IReadOnlyList<SchemaError> errors)
		=> string.Join("; ", errors.Select(error => $"{error.Location}: {error.Kind}"));
}
