using System.Reflection;
using System.Text.RegularExpressions;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

/// <summary>
///     Every issue code is documented in the table the health check links to (docs/configuration.md#issue-codes), the
///     table documents no code that does not exist, and no document or issue template mentions a code that does not exist.
/// </summary>
[TestFixture]
public sealed partial class IssueCodesDocumentationTests
{
	private static IReadOnlyList<string> Codes() => typeof(IssueCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Where(field => field.IsLiteral)
		.Select(field => (string)field.GetRawConstantValue()!)
		.Order(StringComparer.Ordinal)
		.ToList();

	/// <summary>The Markdown documentation and the issue templates.</summary>
	private static IEnumerable<string> DocumentsThatMayMentionCodes() =>
		RepositoryFiles.MarkdownFiles()
			.Concat(Directory.EnumerateFiles(RepositoryFiles.PathOf(".github/ISSUE_TEMPLATE"), "*.yml").Select(RepositoryFiles.ToRelative))
			.Order(StringComparer.Ordinal);

	[Test]
	public void The_issue_code_table_lists_exactly_the_issue_codes()
	{
		var codes = Codes();

		var documentation = RepositoryFiles.Read("docs/configuration.md");
		var tableStart = documentation.IndexOf("## Issue codes", StringComparison.Ordinal);
		Assert.That(tableStart, Is.GreaterThanOrEqualTo(0), "the '## Issue codes' heading (the health check's ReadMoreLink anchor)");
		var documented = TableRow()
			.Matches(documentation[tableStart..])
			.Select(match => match.Groups["code"].Value)
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(codes, Has.Count.GreaterThan(20), "precondition: the codes were found");
			Assert.That(documented, Is.EqualTo(codes));
		});
	}

	[TestCaseSource(nameof(DocumentsThatMayMentionCodes))]
	public void Every_issue_code_a_document_mentions_exists(string file)
	{
		var codes = Codes();

		var unknown = MentionedCode()
			.Matches(RepositoryFiles.Read(file))
			.Select(match => match.Value)
			.Where(code => !codes.Contains(code, StringComparer.Ordinal))
			.Distinct(StringComparer.Ordinal)
			.ToList();

		Assert.That(unknown, Is.Empty, $"issue codes in {file} that IssueCodes does not define");
	}

	[GeneratedRegex(@"^\| `(?<code>PV\d{3})` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex TableRow();

	[GeneratedRegex(@"\bPV\d{3}\b", RegexOptions.CultureInvariant)]
	private static partial Regex MentionedCode();
}
