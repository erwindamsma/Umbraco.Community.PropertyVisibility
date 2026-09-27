using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NJsonSchema;
using NJsonSchema.Generation;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

namespace Umbraco.Community.PropertyVisibility.SchemaGenerator;

/// <summary>One generated schema: its file name and its normalized content (LF line endings, final newline).</summary>
/// <param name="FileName">File name in the package project root and in the nupkg root.</param>
/// <param name="Content">The schema JSON.</param>
internal sealed record GeneratedFile(string FileName, string Content);

/// <summary>
///     Builds the two schemas from the options model with NJsonSchema's System.Text.Json reflection: PascalCase names (the
///     CLR names; <c>[JsonPropertyName]</c> wins, which gives the file's <c>$schema</c>), XML docs as descriptions, defaults
///     from the property initializers, dictionaries as objects whose <c>additionalProperties</c> is the element schema, and
///     <c>"additionalProperties": false</c> on the option objects so editors flag a misspelled key.
/// </summary>
internal static partial class SchemaFiles
{
	/// <summary>Merged into the site's appsettings-schema.json by Umbraco.Cms.Targets.</summary>
	public const string AppSettingsSchemaFileName = "appsettings-schema.Umbraco.Community.PropertyVisibility.json";

	/// <summary>Referenced from the rules file: <c>"$schema": "./PropertyVisibility.config-schema.json"</c>.</summary>
	public const string ConfigFileSchemaFileName = "PropertyVisibility.config-schema.json";

	/// <summary>The package's XML documentation file, next to its assembly; NJsonSchema reads the descriptions from it.</summary>
	public static string DocumentationFile => Path.ChangeExtension(typeof(PropertyVisibilityOptions).Assembly.Location, ".xml");

	/// <summary>Generates both files, deterministically.</summary>
	/// <returns>The appsettings schema, then the rules file schema.</returns>
	public static IReadOnlyList<GeneratedFile> Generate() =>
	[
		new(AppSettingsSchemaFileName, Serialize(AppSettingsSchema())),
		new(ConfigFileSchemaFileName, Serialize(ConfigFileSchema())),
	];

	/// <summary>
	///     Same shape as Umbraco's own appsettings-schema.Umbraco.Cms.json: a root object whose only property is the section,
	///     referencing a definition. The root stays open (no <c>additionalProperties</c>) because the site's
	///     appsettings-schema.json combines it with the other schemas through <c>allOf</c>.
	/// </summary>
	private static JsonSchema AppSettingsSchema()
	{
		var schema = Create(typeof(AppSettingsRoot));
		schema.Title = "UmbracoCommunityPropertyVisibilitySchema";
		schema.Description = null;
		schema.AllowAdditionalProperties = true;
		return schema;
	}

	/// <summary>The rules file: the root is the file model itself, closed, with an optional <c>$schema</c> string.</summary>
	private static JsonSchema ConfigFileSchema()
	{
		var schema = Create(typeof(PropertyVisibilityConfigFile));
		schema.Title = "PropertyVisibilityConfigFile";
		return schema;
	}

	private static JsonSchema Create(Type type)
	{
		var settings = new SystemTextJsonSchemaGeneratorSettings
		{
			SchemaType = SchemaType.JsonSchema,
			UseXmlDocumentation = true,
			GenerateAbstractProperties = false,
			FlattenInheritanceHierarchy = true,
		};
		settings.SchemaProcessors.Add(new ModelDefaultsSchemaProcessor(typeof(PropertyVisibilityOptions).Assembly));

		var schema = new JsonSchemaGenerator(settings).Generate(type);

		Normalize(schema, new HashSet<JsonSchema>());
		SortDefinitions(schema);
		return schema;
	}

	/// <summary>
	///     XML doc text keeps the source's line breaks and indentation; collapse it to one line so editors reflow it and the
	///     output does not depend on the platform that compiled the documentation file.
	/// </summary>
	private static void Normalize(JsonSchema schema, HashSet<JsonSchema> visited)
	{
		if (!visited.Add(schema))
		{
			return;
		}

		schema.Description = CollapseWhitespace(schema.Description);

		foreach (var property in schema.Properties.Values)
		{
			Normalize(property, visited);
		}

		foreach (var definition in schema.Definitions.Values)
		{
			Normalize(definition, visited);
		}

		foreach (var child in schema.AllOf.Concat(schema.OneOf).Concat(schema.AnyOf).Concat(schema.Items))
		{
			Normalize(child, visited);
		}

		if (schema.Item is not null)
		{
			Normalize(schema.Item, visited);
		}

		if (schema.AdditionalPropertiesSchema is not null)
		{
			Normalize(schema.AdditionalPropertiesSchema, visited);
		}
	}

	private static string? CollapseWhitespace(string? text) =>
		string.IsNullOrWhiteSpace(text) ? null : Whitespace().Replace(text, " ").Trim();

	/// <summary>Definitions in ordinal name order, whatever order the generator met the types in.</summary>
	private static void SortDefinitions(JsonSchema schema)
	{
		var sorted = schema.Definitions.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
		schema.Definitions.Clear();
		foreach (var (name, definition) in sorted)
		{
			schema.Definitions.Add(name, definition);
		}
	}

	private static string Serialize(JsonSchema schema) => TextFile.Normalize(schema.ToJson());

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	/// <summary>The appsettings document as far as this package is concerned: one section.</summary>
	private sealed class AppSettingsRoot
	{
		// Read by NJsonSchema through reflection only; the name is the section the options bind from.
		[JsonPropertyName(PropertyVisibilityOptions.SectionName)]
		public PropertyVisibilityOptions Section { get; set; } = new();
	}
}
