using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

[TestFixture]
public sealed class ConfigFileParserTests
{
	[TestCase("", "", 0)]
	[TestCase("abc", "", 3)]
	[TestCase("propertes", "properties", 1)]
	[TestCase("rootnodenme", "rootnodename", 1)]
	[TestCase("rootnodenme", "rootnodekey", 3)]
	[TestCase("kitten", "sitting", 3)]
	public void Levenshtein_counts_single_character_edits(string a, string b, int expected)
		=> Assert.That(ConfigFileParser.Levenshtein(a, b), Is.EqualTo(expected));

	[TestCase("Propertes", "Properties")]
	[TestCase("PROPERTIES2", "Properties")]
	[TestCase("containerz", "Containers")]
	[TestCase("Prps", null, Description = "distance 4 is too far")]
	[TestCase("xyz", null)]
	public void Suggest_returns_the_closest_known_key_within_three_edits(string unknown, string? expected)
		=> Assert.That(ConfigFileParser.Suggest(unknown, ["Properties", "Containers"]), Is.EqualTo(expected));

	[Test]
	public void Suggest_prefers_the_first_candidate_on_a_tie()
		=> Assert.That(ConfigFileParser.Suggest("ab", ["ax", "ay"]), Is.EqualTo("ax"));

	[TestCase("corporate", "$.Sites.corporate")]
	[TestCase("my site", "$.Sites['my site']")]
	[TestCase("a.b", "$.Sites['a.b']")]
	[TestCase("$schema", "$.Sites.$schema")]
	public void Paths_follow_the_System_Text_Json_notation(string name, string expected)
		=> Assert.That(ConfigFileParser.AppendPath("$.Sites", name), Is.EqualTo(expected));

	[Test]
	public void The_deserializer_settings_are_case_insensitive_lenient_on_syntax_and_strict_on_keys()
	{
		System.Text.Json.JsonSerializerOptions options = ConfigFileParser.SerializerOptions;

		Assert.Multiple(() =>
		{
			Assert.That(options.PropertyNameCaseInsensitive, Is.True);
			Assert.That(options.AllowTrailingCommas, Is.True);
			Assert.That(options.ReadCommentHandling, Is.EqualTo(System.Text.Json.JsonCommentHandling.Skip));
			Assert.That(options.UnmappedMemberHandling, Is.EqualTo(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow));
		});
	}
}
