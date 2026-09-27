using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Documentation;

/// <summary>
///     The options table in docs/configuration.md documents exactly the options model (names, types and defaults), and the
///     documented rules file keys are exactly the rules file model's.
/// </summary>
[TestFixture]
public sealed partial class OptionsDocumentationTests
{
	private const string ConfigurationDoc = "docs/configuration.md";

	[Test]
	public void The_options_table_lists_exactly_the_options()
	{
		var documented = DocumentedOptions().Keys.Order(StringComparer.Ordinal).ToList();
		var model = ModelOptions().Select(option => option.Name).Order(StringComparer.Ordinal).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(model, Has.Count.GreaterThanOrEqualTo(10), "precondition: the options were found");
			Assert.That(documented, Is.EqualTo(model));
		});
	}

	[Test]
	public void The_options_table_states_each_type_and_default()
	{
		var documented = DocumentedOptions();
		var mismatches = new List<string>();
		foreach (var option in ModelOptions())
		{
			if (!documented.TryGetValue(option.Name, out var row))
			{
				continue; // reported by The_options_table_lists_exactly_the_options
			}

			if (row.Type != option.Type)
			{
				mismatches.Add($"{option.Name}: type documented as '{row.Type}', the model has '{option.Type}'");
			}

			if (row.Default != option.Default)
			{
				mismatches.Add($"{option.Name}: default documented as '{row.Default}', the model has '{option.Default}'");
			}
		}

		Assert.That(mismatches, Is.Empty);
	}

	[Test]
	public void The_documented_rules_file_keys_are_the_rules_file_model()
	{
		var section = MarkdownDocument.Load(ConfigurationDoc).SectionText("The rules file");
		var allowed = AllowedKeysSentence().Match(section);
		Assert.That(allowed.Success, Is.True, "the '- Allowed keys: ...' bullet in 'The rules file'");

		var documentedAllowed = CodeSpan().Matches(allowed.Groups["allowed"].Value).Select(m => m.Groups["code"].Value).Order(StringComparer.Ordinal).ToList();
		var documentedRejected = CodeSpan().Matches(allowed.Groups["rejected"].Value).Select(m => m.Groups["code"].Value).Order(StringComparer.Ordinal).ToList();

		var fileKeys = typeof(PropertyVisibilityConfigFile)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name)
			.ToList();
		var appSettingsOnly = typeof(PropertyVisibilityOptions)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.CanWrite)
			.Select(property => property.Name)
			.Except(fileKeys, StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(documentedAllowed, Is.EqualTo(fileKeys.Order(StringComparer.Ordinal).ToList()), "allowed keys");
			Assert.That(documentedRejected, Is.EqualTo(appSettingsOnly), "keys rejected in the file (appsettings only)");
		});
	}

	/// <summary>The rows of the options table, keyed by the option path in its first cell.</summary>
	private static Dictionary<string, (string Type, string Default)> DocumentedOptions()
	{
		var rows = MarkdownDocument.Load(ConfigurationDoc).TableRowsAfterHeading("Options");
		var options = new Dictionary<string, (string Type, string Default)>(StringComparer.Ordinal);
		foreach (var cells in rows)
		{
			var name = CodeSpan().Match(cells[0]);
			Assert.That(name.Success, Is.True, $"option name in backticks: {cells[0]}");
			Assert.That(options.TryAdd(name.Groups["code"].Value, (cells[1], cells[2])), Is.True, $"documented once: {cells[0]}");
		}

		return options;
	}

	/// <summary>
	///     Every settable option of the model as the table names it: <c>Name</c> for the root options,
	///     <c>Sites:&lt;label&gt;:Name</c> for a site, <c>&lt;content type&gt;:Name</c> for a content type rule, with the
	///     table's notation for its type and default.
	/// </summary>
	private static IEnumerable<(string Name, string Type, string Default)> ModelOptions()
	{
		IEnumerable<(string, string, string)> Describe(Type type, string prefix, object instance) =>
			type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(property => property.CanWrite && property.GetSetMethod() is not null)
				.Select(property => (prefix + property.Name, TypeNotation(property.PropertyType), DefaultNotation(property.GetValue(instance))));

		return Describe(typeof(PropertyVisibilityOptions), string.Empty, new PropertyVisibilityOptions())
			.Concat(Describe(typeof(SiteVisibilityOptions), "Sites:<label>:", new SiteVisibilityOptions()))
			.Concat(Describe(typeof(ContentTypeVisibilityOptions), "<content type>:", new ContentTypeVisibilityOptions()));
	}

	private static string TypeNotation(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type) ?? type;
		if (underlying == typeof(bool))
		{
			return "bool";
		}

		if (underlying == typeof(string))
		{
			return "string";
		}

		if (underlying == typeof(Guid))
		{
			return "GUID";
		}

		if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Dictionary<,>))
		{
			return "object";
		}

		if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>) && underlying.GetGenericArguments()[0] == typeof(string))
		{
			return "string[]";
		}

		throw new InvalidOperationException($"No table notation for option type {type}; extend the test and the table.");
	}

	private static string DefaultNotation(object? value) => value switch
	{
		null => "none",
		bool flag => flag ? "`true`" : "`false`",
		string text => $"`{text}`",
		ICollection { Count: 0 } => "empty",
		_ => throw new InvalidOperationException($"No table notation for the default {value}; extend the test and the table."),
	};

	[GeneratedRegex(@"`(?<code>[^`]+)`")]
	private static partial Regex CodeSpan();

	[GeneratedRegex(@"^- Allowed keys: (?<allowed>.*?)\. (?<rejected>.*?) are rejected", RegexOptions.Multiline)]
	private static partial Regex AllowedKeysSentence();
}
