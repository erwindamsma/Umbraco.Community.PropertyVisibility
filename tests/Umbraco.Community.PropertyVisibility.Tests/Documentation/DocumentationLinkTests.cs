using System.Text.RegularExpressions;
using Umbraco.Community.PropertyVisibility.HealthChecks;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Documentation;

/// <summary>
///     Links in the documentation resolve: relative links and images point to files, folders and headings that exist
///     (with GitHub's case-sensitive paths), links to this repository's <c>main</c> branch point to files that exist,
///     and the files shown outside the repository use absolute links only. External sites are not contacted.
/// </summary>
[TestFixture]
public sealed partial class DocumentationLinkTests
{
	/// <summary>
	///     Rendered outside the repository: NuGet (the package readme), the Umbraco Marketplace (its readme) and GitHub
	///     release notes (a CHANGELOG section). None of them resolves a relative link.
	/// </summary>
	private static readonly string[] FilesShownOutsideTheRepository =
		["docs/README_nuget.md", "umbraco-marketplace-readme.md", "CHANGELOG.md"];

	private static IEnumerable<string> MarkdownFiles() => RepositoryFiles.MarkdownFiles();

	/// <summary>Markdown files plus the other files that carry links to this repository.</summary>
	private static IEnumerable<string> FilesWithRepositoryLinks() =>
		RepositoryFiles.MarkdownFiles()
			.Concat(Directory.EnumerateFiles(RepositoryFiles.PathOf(".github/ISSUE_TEMPLATE"), "*.yml").Select(RepositoryFiles.ToRelative))
			.Append("umbraco-marketplace.json")
			.Append("src/Umbraco.Community.PropertyVisibility/Umbraco.Community.PropertyVisibility.csproj")
			.Order(StringComparer.Ordinal);

	[Test]
	public void The_documentation_files_are_scanned()
	{
		Assert.That(RepositoryFiles.MarkdownFiles(), Is.SupersetOf(new[]
		{
			".github/PULL_REQUEST_TEMPLATE.md",
			"CHANGELOG.md",
			"CONTRIBUTING.md",
			"README.md",
			"SECURITY.md",
			"docs/README_nuget.md",
			"docs/compatibility.md",
			"docs/configuration.md",
			"docs/testing.md",
			"umbraco-marketplace-readme.md",
		}));
	}

	[TestCaseSource(nameof(MarkdownFiles))]
	public void Relative_links_and_images_resolve(string file)
	{
		var document = MarkdownDocument.Load(file);

		var broken = document.Links
			.Where(link => !IsAbsolute(link.Target))
			.Select(link => (link, problem: CheckRelativeLink(document, link.Target)))
			.Where(result => result.problem is not null)
			.Select(result => $"{file}:{result.link.Line}: {result.link.Target}: {result.problem}")
			.ToList();

		Assert.That(broken, Is.Empty);
	}

	[TestCaseSource(nameof(FilesWithRepositoryLinks))]
	public void Links_to_this_repository_on_main_resolve(string file)
	{
		var broken = RepositoryUrl().Matches(RepositoryFiles.Read(file))
			.Select(match => match.Value.TrimEnd('.', ',', ';', ':'))
			.Distinct(StringComparer.Ordinal)
			.Select(url => (url, problem: CheckRepositoryUrl(url)))
			.Where(result => result.problem is not null)
			.Select(result => $"{file}: {result.url}: {result.problem}")
			.ToList();

		Assert.That(broken, Is.Empty);
	}

	[TestCase(PropertyVisibilityHealthCheck.IssueCodesDocumentationUrl)]
	[TestCase(Constants.CompatibilityDocumentationUrl)]
	public void Documentation_links_in_the_code_resolve(string url)
	{
		Assert.That(RepositoryUrl().Match(url).Value, Is.EqualTo(url), "a link to this repository");
		Assert.That(CheckRepositoryUrl(url), Is.Null);
	}

	[TestCaseSource(nameof(FilesShownOutsideTheRepository))]
	public void Files_shown_outside_the_repository_use_absolute_links_only(string file)
	{
		var relative = MarkdownDocument.Load(file).Links
			.Where(link => !IsAbsolute(link.Target))
			.Select(link => $"{file}:{link.Line}: {link.Target}")
			.ToList();

		Assert.That(relative, Is.Empty);
	}

	[Test]
	public void The_anchor_of_a_heading_is_the_one_GitHub_generates()
	{
		Assert.Multiple(() =>
		{
			Assert.That(MarkdownDocument.GitHubAnchor("Integration: the applied event"), Is.EqualTo("integration-the-applied-event"));
			Assert.That(MarkdownDocument.GitHubAnchor("Publication checklist: accepted exceptions"), Is.EqualTo("publication-checklist-accepted-exceptions"));
			Assert.That(MarkdownDocument.GitHubAnchor("Upgrading to Umbraco 18"), Is.EqualTo("upgrading-to-umbraco-18"));
			Assert.That(MarkdownDocument.GitHubAnchor("The `ContentTypes` option"), Is.EqualTo("the-contenttypes-option"));
			Assert.That(MarkdownDocument.GitHubAnchor("What was re-checked for 17.7.0"), Is.EqualTo("what-was-re-checked-for-1770"));
			Assert.That(MarkdownDocument.GitHubAnchor("See [the docs](docs/configuration.md)"), Is.EqualTo("see-the-docs"));
		});
	}

	private static bool IsAbsolute(string target) => UriScheme().IsMatch(target);

	/// <summary>Null when a relative link resolves, otherwise what is wrong with it.</summary>
	private static string? CheckRelativeLink(MarkdownDocument document, string target)
	{
		var hashIndex = target.IndexOf('#', StringComparison.Ordinal);
		var path = Uri.UnescapeDataString(hashIndex < 0 ? target : target[..hashIndex]);
		var anchor = hashIndex < 0 ? null : target[(hashIndex + 1)..];

		if (path.Length == 0)
		{
			return anchor is null || document.Anchors.Contains(anchor) ? null : $"no heading with the anchor '#{anchor}' in this file";
		}

		if (path.StartsWith('/'))
		{
			return "a root-relative path; use a path relative to this file";
		}

		var directory = Path.GetDirectoryName(RepositoryFiles.PathOf(document.RelativePath))!;
		var fullPath = Path.GetFullPath(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(RepositoryFiles.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			return "points outside the repository";
		}

		var relativePath = RepositoryFiles.ToRelative(fullPath);
		if (!RepositoryFiles.ExistsWithExactCase(relativePath))
		{
			return $"'{relativePath}' does not exist (paths are case-sensitive)";
		}

		return CheckAnchor(relativePath, anchor);
	}

	/// <summary>
	///     Null when a link to this repository resolves or is not checked (issues, advisories, another branch or a tag),
	///     otherwise what is wrong with it.
	/// </summary>
	private static string? CheckRepositoryUrl(string url)
	{
		var match = RepositoryUrl().Match(url);
		if (!match.Success)
		{
			return "not a link to this repository";
		}

		var rest = match.Groups["rest"].Value;
		var hashIndex = rest.IndexOf('#', StringComparison.Ordinal);
		var anchor = hashIndex < 0 ? null : rest[(hashIndex + 1)..];
		var path = hashIndex < 0 ? rest : rest[..hashIndex];
		var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
		if (queryIndex >= 0)
		{
			path = path[..queryIndex];
		}

		path = Uri.UnescapeDataString(path).TrimEnd('/');

		if (match.Groups["host"].Value == "raw.githubusercontent.com")
		{
			if (path == "/main")
			{
				// The prefix of every raw link on main, as the docs quote it ("https://raw.githubusercontent.com/.../main/...").
				return null;
			}

			if (!path.StartsWith("/main/", StringComparison.Ordinal))
			{
				return path.StartsWith("/v", StringComparison.Ordinal) ? null : "a raw link that is not on main or a version tag";
			}

			var file = path["/main/".Length..];
			return RepositoryFiles.ExistsWithExactCase(file, allowDirectory: false) ? null : $"'{file}' does not exist on main (case-sensitive)";
		}

		if (path.Length == 0)
		{
			// The repository home shows README.md; #readme is GitHub's own anchor for it.
			return anchor is null or "readme" ? null : CheckAnchor("README.md", anchor);
		}

		foreach (var (prefix, allowDirectory) in new[] { ("/blob/main/", false), ("/tree/main/", true) })
		{
			if (path.StartsWith(prefix, StringComparison.Ordinal))
			{
				var target = path[prefix.Length..];
				if (!RepositoryFiles.ExistsWithExactCase(target, allowDirectory) || (allowDirectory && !Directory.Exists(RepositoryFiles.PathOf(target))))
				{
					return $"'{target}' does not exist on main as a {(allowDirectory ? "folder" : "file")} (case-sensitive)";
				}

				return CheckAnchor(target, anchor);
			}
		}

		// Issues, security advisories, releases, another branch or a tag: not a file of this checkout.
		return null;
	}

	private static string? CheckAnchor(string relativePath, string? anchor)
	{
		if (string.IsNullOrEmpty(anchor))
		{
			return null;
		}

		if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
		{
			// GitHub's line anchors on a source file.
			return LineAnchor().IsMatch(anchor) ? null : $"anchor '#{anchor}' on a file that is not Markdown";
		}

		return MarkdownDocument.Load(relativePath).Anchors.Contains(anchor) ? null : $"no heading with the anchor '#{anchor}' in {relativePath}";
	}

	[GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:")]
	private static partial Regex UriScheme();

	[GeneratedRegex(@"^L\d+(-L\d+)?$")]
	private static partial Regex LineAnchor();

	[GeneratedRegex(@"https://(?<host>github\.com|raw\.githubusercontent\.com)/erwindamsma/Umbraco\.Community\.PropertyVisibility(?<rest>[/#?][^\s""'<>()\[\]`]*)?")]
	private static partial Regex RepositoryUrl();
}
