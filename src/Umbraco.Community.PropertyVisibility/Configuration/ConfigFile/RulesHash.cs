using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     SHA-256 fingerprint of the effective rules, so the health check can show whether two servers (or two loads) run the
///     same rules.
/// </summary>
/// <remarks>
///     Covers <see cref="PropertyVisibilityOptions.HideEmptiedContainers" />, <see cref="PropertyVisibilityOptions.ContentTypes" />,
///     <see cref="PropertyVisibilityOptions.RuleSets" /> and <see cref="PropertyVisibilityOptions.Sites" />, written as
///     canonical JSON: dictionary keys and alias lists (a site's <see cref="SiteVisibilityOptions.Include" /> too) sorted
///     ordinally, no whitespace. Formatting, comments and ordering of the source therefore do not change the hash; any
///     change of a key, alias, rule set name, root key, root name or flag does. Rule sets and a site's
///     <see cref="SiteVisibilityOptions.Include" /> are only written when they are not empty, so rules that use neither
///     hash as they did before rule sets existed. <see cref="PropertyVisibilityOptions.Enabled" /> and
///     <see cref="PropertyVisibilityOptions.ConfigFile" /> are not rules and are left out.
/// </remarks>
internal static class RulesHash
{
	/// <summary>
	///     Computes the hash.
	/// </summary>
	/// <param name="options">The effective options.</param>
	/// <returns>Lower-case hex SHA-256.</returns>
	public static string Compute(PropertyVisibilityOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var buffer = new ArrayBufferWriter<byte>();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteBoolean(nameof(PropertyVisibilityOptions.HideEmptiedContainers), options.HideEmptiedContainers);
			writer.WritePropertyName(nameof(PropertyVisibilityOptions.ContentTypes));
			WriteContentTypes(writer, options.ContentTypes);

			// Written only when present, so rules without rule sets keep the hash they had before rule sets existed.
			if (options.RuleSets is { Count: > 0 })
			{
				writer.WritePropertyName(nameof(PropertyVisibilityOptions.RuleSets));
				writer.WriteStartObject();
				foreach ((var name, RuleSetOptions? ruleSet) in Sorted(options.RuleSets))
				{
					writer.WritePropertyName(name);
					writer.WriteStartObject();
					writer.WritePropertyName(nameof(RuleSetOptions.ContentTypes));
					WriteContentTypes(writer, ruleSet?.ContentTypes);
					writer.WriteEndObject();
				}

				writer.WriteEndObject();
			}

			writer.WritePropertyName(nameof(PropertyVisibilityOptions.Sites));
			writer.WriteStartObject();
			foreach ((var label, SiteVisibilityOptions? site) in Sorted(options.Sites))
			{
				writer.WritePropertyName(label);
				writer.WriteStartObject();
				if (site?.RootNodeKey is { } rootNodeKey)
				{
					writer.WriteString(nameof(SiteVisibilityOptions.RootNodeKey), rootNodeKey.ToString("D"));
				}

				if (site?.RootNodeName is { } rootNodeName)
				{
					writer.WriteString(nameof(SiteVisibilityOptions.RootNodeName), rootNodeName);
				}

				writer.WriteBoolean(nameof(SiteVisibilityOptions.IsDefault), site?.IsDefault ?? false);
				if (site?.Include is { Count: > 0 } include)
				{
					WriteAliases(writer, nameof(SiteVisibilityOptions.Include), include);
				}

				writer.WritePropertyName(nameof(SiteVisibilityOptions.ContentTypes));
				WriteContentTypes(writer, site?.ContentTypes);
				writer.WriteEndObject();
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
	}

	private static void WriteContentTypes(Utf8JsonWriter writer, Dictionary<string, ContentTypeVisibilityOptions>? contentTypes)
	{
		writer.WriteStartObject();
		foreach ((var alias, ContentTypeVisibilityOptions? block) in Sorted(contentTypes))
		{
			writer.WritePropertyName(alias);
			writer.WriteStartObject();
			WriteAliases(writer, nameof(ContentTypeVisibilityOptions.Properties), block?.Properties);
			WriteAliases(writer, nameof(ContentTypeVisibilityOptions.Containers), block?.Containers);
			writer.WriteEndObject();
		}

		writer.WriteEndObject();
	}

	private static void WriteAliases(Utf8JsonWriter writer, string name, List<string>? aliases)
	{
		writer.WriteStartArray(name);
		foreach (var alias in (aliases ?? []).Where(alias => alias is not null).Order(StringComparer.Ordinal))
		{
			writer.WriteStringValue(alias);
		}

		writer.WriteEndArray();
	}

	private static IEnumerable<KeyValuePair<string, TValue?>> Sorted<TValue>(Dictionary<string, TValue>? dictionary)
		where TValue : class
		=> (dictionary ?? [])
			.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => new KeyValuePair<string, TValue?>(entry.Key, entry.Value));
}
