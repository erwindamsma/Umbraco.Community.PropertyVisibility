using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Result of parsing the rules file: the rules, or the issues that prevented them.
/// </summary>
/// <param name="Rules">The parsed rules; <c>null</c> when <paramref name="Issues" /> is not empty.</param>
/// <param name="Issues">The problems found; empty when the file parsed.</param>
internal sealed record ConfigFileParseResult(ConfigFileRules? Rules, IReadOnlyList<ConfigurationIssue> Issues)
{
	public static ConfigFileParseResult Success(ConfigFileRules rules) => new(rules, []);

	public static ConfigFileParseResult Failure(IReadOnlyList<ConfigurationIssue> issues) => new(null, issues);

	public static ConfigFileParseResult Failure(ConfigurationIssue issue) => new(null, [issue]);
}

/// <summary>
///     Parses the rules file into <see cref="ConfigFileRules" />.
/// </summary>
/// <remarks>
///     Four passes over the UTF-8 content (<see cref="ToUtf8" /> transcodes a UTF-16 or UTF-32 file first), each
///     stopping at its first kind of failure:
///     <list type="number">
///         <item>
///             a UTF-8 check (<see cref="IssueCodes.InvalidJson" /> with the line and position of the first invalid byte and
///             the advice to save the file as UTF-8);
///         </item>
///         <item><see cref="JsonDocument" /> for syntax (<see cref="IssueCodes.InvalidJson" /> with line and position);</item>
///         <item>
///             a walk guided by the <see cref="PropertyVisibilityConfigFile" /> model that reports every unknown key with its
///             JSON path and a did-you-mean suggestion (<see cref="IssueCodes.UnknownKey" />), and every key given twice
///             (keys are case-insensitive, <see cref="IssueCodes.InvalidJson" />);
///         </item>
///         <item>
///             <see cref="JsonSerializer" /> with <see cref="JsonUnmappedMemberHandling.Disallow" /> for value types
///             (<see cref="IssueCodes.InvalidJson" /> with the JSON path, line and position).
///         </item>
///     </list>
/// </remarks>
internal static class ConfigFileParser
{
	/// <summary>Largest edit distance for which a known key is suggested.</summary>
	public const int MaxSuggestionDistance = 3;

	/// <summary>The deserializer settings: case-insensitive, comments and trailing commas allowed, unknown keys rejected.</summary>
	public static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
	};

	private static readonly JsonDocumentOptions DocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip,
	};

	private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
	private static readonly byte[] JsonWhitespace = [(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n'];

	// Characters that force bracket notation in a JSON path, as System.Text.Json writes them.
	private static readonly SearchValues<char> PathSpecialCharacters = SearchValues.Create([.. ".' /\"[]()\t\n\r\f\b\\\u0085", (char)0x2028, (char)0x2029]);

	private static readonly ConcurrentDictionary<Type, IReadOnlyList<ModelMember>> MembersByType = new();

	/// <summary>
	///     Returns the content as UTF-8. A file that starts with a UTF-16 or UTF-32 byte order mark (Windows PowerShell 5.1
	///     writes UTF-16 by default) is transcoded, as the appsettings JSON provider does; anything else is returned as is.
	/// </summary>
	/// <param name="content">The raw file content.</param>
	/// <returns>UTF-8 bytes, possibly with a UTF-8 byte order mark; the same array when no transcoding was needed.</returns>
	public static byte[] ToUtf8(byte[] content)
	{
		ArgumentNullException.ThrowIfNull(content);

		(Encoding Encoding, int BomLength)? source = content switch
		{
			[0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4),
			[0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4),
			[0xFF, 0xFE, ..] => (Encoding.Unicode, 2),
			[0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2),
			_ => null,
		};

		return source is { } found
			? Encoding.UTF8.GetBytes(found.Encoding.GetString(content, found.BomLength, content.Length - found.BomLength))
			: content;
	}

	/// <summary>
	///     Whether the content holds no JSON value at all: empty, whitespace or only comments. Such a file counts as absent,
	///     so a fully commented-out sample file leaves the appsettings rules in effect.
	/// </summary>
	/// <param name="utf8">The raw file content.</param>
	/// <returns><c>true</c> when there is nothing to parse.</returns>
	public static bool HasNoJsonValue(ReadOnlySpan<byte> utf8)
	{
		ReadOnlySpan<byte> json = StripBom(utf8).Trim(JsonWhitespace);
		if (json.IsEmpty)
		{
			return true;
		}

		var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow });
		try
		{
			while (reader.Read())
			{
				if (reader.TokenType != JsonTokenType.Comment)
				{
					return false;
				}
			}

			return true;
		}
		catch (JsonException)
		{
			// Not only comments: the real parse reports the syntax error with its position.
			return false;
		}
	}

	/// <summary>
	///     Parses the rules file.
	/// </summary>
	/// <param name="utf8">The raw file content (a UTF-8 byte order mark is allowed).</param>
	/// <param name="displayName">The file name as configured, used in messages.</param>
	/// <returns>The rules, or the issues.</returns>
	public static ConfigFileParseResult Parse(ReadOnlyMemory<byte> utf8, string displayName)
	{
		ReadOnlyMemory<byte> json = StripBom(utf8);

		// A file saved in a legacy code page (Windows-1252 and the like) with a non-ASCII character: System.Text.Json
		// would report it as an unrelated error, or not at all until a name is read.
		if (FirstInvalidUtf8Byte(json.Span) is { } invalid)
		{
			(var line, var position) = LineAndPosition(json.Span, invalid);
			return ConfigFileParseResult.Failure(NotUtf8(displayName, $" at line {line}, position {position}"));
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return ConfigFileParseResult.Failure(new ConfigurationIssue(
					IssueCodes.InvalidJson,
					IssueSeverity.Error,
					$"Rules file '{displayName}' must contain a JSON object, not {Describe(document.RootElement.ValueKind)}.",
					"$"));
			}

			var issues = new List<ConfigurationIssue>();
			Walk(document.RootElement, typeof(PropertyVisibilityConfigFile), "$", issues);
			if (issues.Count > 0)
			{
				return ConfigFileParseResult.Failure(issues);
			}
		}
		catch (JsonException ex)
		{
			return ConfigFileParseResult.Failure(CannotParse(ex, displayName));
		}
		catch (InvalidOperationException)
		{
			// Reading a property name that cannot be transcoded to UTF-16; the check above makes this unexpected.
			return ConfigFileParseResult.Failure(NotUtf8(displayName, string.Empty));
		}

		try
		{
			PropertyVisibilityConfigFile? file = JsonSerializer.Deserialize<PropertyVisibilityConfigFile>(json.Span, SerializerOptions);
			return file is null
				? ConfigFileParseResult.Failure(new ConfigurationIssue(IssueCodes.InvalidJson, IssueSeverity.Error, $"Rules file '{displayName}' must contain a JSON object.", "$"))
				: ConfigFileParseResult.Success(ConfigFileRules.From(file));
		}
		catch (JsonException ex)
		{
			return ConfigFileParseResult.Failure(CannotParse(ex, displayName));
		}
	}

	/// <summary>
	///     Returns the candidate closest to <paramref name="unknown" /> (case-insensitive Levenshtein distance), when that
	///     distance is at most <see cref="MaxSuggestionDistance" />; the first candidate wins a tie.
	/// </summary>
	/// <param name="unknown">The unknown key.</param>
	/// <param name="candidates">The known keys at the same level, in declaration order.</param>
	/// <returns>The suggestion, or <c>null</c>.</returns>
	public static string? Suggest(string unknown, IEnumerable<string> candidates)
	{
		string? best = null;
		var bestDistance = MaxSuggestionDistance + 1;
		foreach (var candidate in candidates)
		{
			var distance = Levenshtein(unknown.ToLowerInvariant(), candidate.ToLowerInvariant());
			if (distance < bestDistance)
			{
				best = candidate;
				bestDistance = distance;
			}
		}

		return best;
	}

	/// <summary>
	///     Levenshtein edit distance (insertions, deletions and substitutions each cost 1).
	/// </summary>
	/// <param name="a">The first string.</param>
	/// <param name="b">The second string.</param>
	/// <returns>The distance.</returns>
	public static int Levenshtein(string a, string b)
	{
		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];
		for (var j = 0; j <= b.Length; j++)
		{
			previous[j] = j;
		}

		for (var i = 1; i <= a.Length; i++)
		{
			current[0] = i;
			for (var j = 1; j <= b.Length; j++)
			{
				var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
				current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
			}

			(previous, current) = (current, previous);
		}

		return previous[b.Length];
	}

	/// <summary>
	///     Appends a property name to a JSON path the way System.Text.Json writes paths: <c>$.a.b</c>, or <c>$['a b']</c>
	///     for a name with special characters.
	/// </summary>
	/// <param name="path">The parent path.</param>
	/// <param name="name">The property name.</param>
	/// <returns>The child path.</returns>
	public static string AppendPath(string path, string name)
		=> name.AsSpan().IndexOfAny(PathSpecialCharacters) >= 0 ? $"{path}['{name}']" : $"{path}.{name}";

	private static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> utf8) => utf8.StartsWith(Utf8Bom) ? utf8[Utf8Bom.Length..] : utf8;

	// Index of the first byte that does not start a valid UTF-8 sequence (or starts one that is cut off); null when the
	// content is valid UTF-8.
	private static int? FirstInvalidUtf8Byte(ReadOnlySpan<byte> utf8)
	{
		if (System.Text.Unicode.Utf8.IsValid(utf8))
		{
			return null;
		}

		var index = 0;
		while (index < utf8.Length)
		{
			if (Rune.DecodeFromUtf8(utf8[index..], out _, out var consumed) != OperationStatus.Done)
			{
				return index;
			}

			index += consumed;
		}

		return null;
	}

	// One-based line and byte position in that line, as the JSON error messages count them.
	private static (int Line, int Position) LineAndPosition(ReadOnlySpan<byte> utf8, int index)
	{
		ReadOnlySpan<byte> before = utf8[..index];
		var lastNewLine = before.LastIndexOf((byte)'\n');
		return (before.Count((byte)'\n') + 1, index - lastNewLine);
	}

	private static ConfigurationIssue NotUtf8(string displayName, string location)
		=> new(
			IssueCodes.InvalidJson,
			IssueSeverity.Error,
			$"Rules file '{displayName}' is not valid UTF-8{location}: it was probably saved in another encoding (such as Windows-1252). Save the file as UTF-8.",
			"$");

	private static ReadOnlyMemory<byte> StripBom(ReadOnlyMemory<byte> utf8) => utf8.Span.StartsWith(Utf8Bom) ? utf8[Utf8Bom.Length..] : utf8;

	private static ConfigurationIssue CannotParse(JsonException ex, string displayName)
	{
		var location = ex.LineNumber is { } line
			? $" at line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}"
			: string.Empty;
		return new ConfigurationIssue(
			IssueCodes.InvalidJson,
			IssueSeverity.Error,
			$"Rules file '{displayName}' cannot be parsed{location}: {WithoutLocation(ex.Message)}",
			string.IsNullOrEmpty(ex.Path) ? null : ex.Path);
	}

	// System.Text.Json appends "Path: ... | LineNumber: n | BytePositionInLine: n." (zero-based) to its messages; the issue
	// carries the path separately and a one-based location in the text.
	private static string WithoutLocation(string message)
	{
		var cut = message.Length;
		foreach (var marker in new[] { " Path: ", " LineNumber: " })
		{
			var index = message.IndexOf(marker, StringComparison.Ordinal);
			if (index >= 0 && index < cut)
			{
				cut = index;
			}
		}

		return message[..cut].TrimEnd();
	}

	private static string Describe(JsonValueKind kind) => kind switch
	{
		JsonValueKind.Array => "an array",
		JsonValueKind.String => "a string",
		JsonValueKind.Number => "a number",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		JsonValueKind.Null => "null",
		_ => kind.ToString(),
	};

	private static void Walk(JsonElement element, Type type, string path, List<ConfigurationIssue> issues)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;

		if (element.ValueKind == JsonValueKind.Object && TryGetDictionaryValueType(type, out Type? valueType))
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty property in element.EnumerateObject())
			{
				var childPath = AppendPath(path, property.Name);
				if (!seen.Add(property.Name))
				{
					issues.Add(Duplicate(property.Name, childPath));
					continue;
				}

				Walk(property.Value, valueType, childPath, issues);
			}
		}
		else if (element.ValueKind == JsonValueKind.Object && IsModel(type))
		{
			IReadOnlyList<ModelMember> members = MembersByType.GetOrAdd(type, GetMembers);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty property in element.EnumerateObject())
			{
				var childPath = AppendPath(path, property.Name);
				ModelMember? member = members.FirstOrDefault(m => string.Equals(m.JsonName, property.Name, StringComparison.OrdinalIgnoreCase));
				if (member is null)
				{
					issues.Add(Unknown(property.Name, childPath, type, members));
					continue;
				}

				if (!seen.Add(member.JsonName))
				{
					issues.Add(Duplicate(property.Name, childPath));
					continue;
				}

				Walk(property.Value, member.Type, childPath, issues);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array && TryGetElementType(type, out Type? elementType))
		{
			var index = 0;
			foreach (JsonElement item in element.EnumerateArray())
			{
				Walk(item, elementType, $"{path}[{index++}]", issues);
			}
		}

		// Anything else is a scalar, or a value of the wrong kind that the deserializer reports with its position.
	}

	private static ConfigurationIssue Unknown(string name, string path, Type owner, IReadOnlyList<ModelMember> members)
	{
		if (owner == typeof(PropertyVisibilityConfigFile))
		{
			if (string.Equals(name, nameof(PropertyVisibilityOptions.Enabled), StringComparison.OrdinalIgnoreCase)
				|| string.Equals(name, nameof(PropertyVisibilityOptions.ConfigFile), StringComparison.OrdinalIgnoreCase))
			{
				return new ConfigurationIssue(
					IssueCodes.UnknownKey,
					IssueSeverity.Error,
					$"'{name}' can only be set in appsettings ({PropertyVisibilityOptions.SectionName}:{name}), not in the rules file.",
					path);
			}

			if (string.Equals(name, PropertyVisibilityOptions.SectionName, StringComparison.OrdinalIgnoreCase))
			{
				return new ConfigurationIssue(
					IssueCodes.UnknownKey,
					IssueSeverity.Error,
					$"The rules file holds the content of the {PropertyVisibilityOptions.SectionName} section directly; remove the '{name}' wrapper.",
					path);
			}
		}

		return new ConfigurationIssue(
			IssueCodes.UnknownKey,
			IssueSeverity.Error,
			$"Unknown key '{name}'. Known keys here: {string.Join(", ", members.Select(m => m.JsonName))}.",
			path,
			Suggest(name, members.Select(m => m.JsonName)));
	}

	private static ConfigurationIssue Duplicate(string name, string path)
		=> new(IssueCodes.InvalidJson, IssueSeverity.Error, $"Key '{name}' is given more than once at this level (keys are case-insensitive).", path);

	private static bool IsModel(Type type) => type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type);

	private static bool TryGetDictionaryValueType(Type type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? valueType)
	{
		Type? dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
			? type
			: type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
		valueType = dictionary?.GetGenericArguments()[1];
		return valueType is not null;
	}

	private static bool TryGetElementType(Type type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? elementType)
	{
		if (type == typeof(string))
		{
			elementType = null;
			return false;
		}

		elementType = type.IsArray
			? type.GetElementType()
			: type.GetInterfaces()
				.Append(type)
				.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				?.GetGenericArguments()[0];
		return elementType is not null;
	}

	private static IReadOnlyList<ModelMember> GetMembers(Type type)
		=> type
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.SetMethod is { IsPublic: true } && property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
			.OrderBy(property => property.MetadataToken)
			.Select(property => new ModelMember(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name, property.PropertyType))
			.ToList();

	private sealed record ModelMember(string JsonName, Type Type);
}
