using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     Validates JSON documents against the package's committed JSON schemas with NJsonSchema (the library the schema
///     generator uses as well). The schemas declare draft-04, like Umbraco's own appsettings schemas, which NJsonSchema
///     implements.
/// </summary>
internal static class JsonSchemaValidation
{
	private static readonly JsonDocumentOptions JsonWithComments = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);

	/// <summary>
	///     Validates a document, which may hold comments and trailing commas as appsettings and the rules file may.
	/// </summary>
	/// <param name="schemaJson">The schema's text.</param>
	/// <param name="json">The document.</param>
	/// <returns>Every failure, the nested ones included; empty when the document is valid.</returns>
	public static IReadOnlyList<SchemaError> Validate(string schemaJson, string json)
	{
		JsonSchema schema = Schemas.GetOrAdd(schemaJson, static text => JsonSchema.FromJsonAsync(text).GetAwaiter().GetResult());

		// NJsonSchema reads the document with Newtonsoft.Json; hand it plain JSON.
		var plain = JsonNode.Parse(json, documentOptions: JsonWithComments)?.ToJsonString() ?? "null";
		return Flatten(schema.Validate(plain)).ToList();
	}

	/// <summary>Formats the failures for an assertion message, one per line.</summary>
	/// <param name="errors">The failures.</param>
	/// <returns>The text.</returns>
	public static string Describe(IEnumerable<SchemaError> errors)
		=> string.Join(Environment.NewLine, errors.Select(error => $"{error.Location}: {error.Kind}"));

	private static IEnumerable<SchemaError> Flatten(IEnumerable<ValidationError> errors)
	{
		foreach (ValidationError error in errors)
		{
			yield return new SchemaError(Pointer(error), error.Kind);
			if (error is ChildSchemaValidationError child)
			{
				foreach (SchemaError nested in Flatten(child.Errors.Values.SelectMany(childErrors => childErrors)))
				{
					yield return nested;
				}
			}
		}
	}

	// The JSON pointer of the failing value (or of the key, for a key the schema does not allow).
	private static string Pointer(ValidationError error)
	{
		if (error.Token is null)
		{
			var path = error.Path?.TrimStart('#', '/') ?? string.Empty;
			return path.Length == 0 ? string.Empty : "/" + path.Replace('.', '/');
		}

		var segments = new List<string>();
		for (JToken? current = error.Token; current is not null; current = current.Parent)
		{
			if (current is JProperty property)
			{
				segments.Add(property.Name);
			}
			else if (current.Parent is JArray array)
			{
				segments.Add(array.IndexOf(current).ToString(CultureInfo.InvariantCulture));
			}
		}

		segments.Reverse();
		return string.Concat(segments.Select(segment => "/" + segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)));
	}
}

/// <summary>
///     One schema validation failure.
/// </summary>
/// <param name="Location">JSON pointer of the failing value, such as <c>/PropertyVisibility/HideEmptiedContainer</c>.</param>
/// <param name="Kind">What failed.</param>
internal sealed record SchemaError(string Location, ValidationErrorKind Kind);
