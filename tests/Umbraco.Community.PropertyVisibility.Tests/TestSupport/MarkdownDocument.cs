using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>A fenced code block: its info string (the language), its content and the line of its opening fence.</summary>
internal sealed record MarkdownCodeBlock(string Info, string Content, int Line);

/// <summary>A link or image target and the (1-based) line it is on.</summary>
internal sealed record MarkdownLink(string Target, int Line);

/// <summary>
///     Just enough Markdown for the documentation checks: fenced code blocks, ATX headings and the anchors GitHub gives
///     them, link and image targets outside code, and the rows of a table under a heading.
/// </summary>
internal sealed partial class MarkdownDocument
{
	private readonly string[] _proseLines;

	private MarkdownDocument(string relativePath, string text)
	{
		RelativePath = relativePath;
		var lines = text.ReplaceLineEndings("\n").Split('\n');
		_proseLines = new string[lines.Length];
		var codeBlocks = new List<MarkdownCodeBlock>();

		for (var index = 0; index < lines.Length; index++)
		{
			var open = FenceOpen().Match(lines[index]);
			if (!open.Success)
			{
				_proseLines[index] = lines[index];
				continue;
			}

			// A fenced code block: blank it out of the prose (line numbers stay), keep its content.
			var fence = open.Groups["fence"].Value;
			var start = index;
			var content = new StringBuilder();
			_proseLines[index] = string.Empty;
			for (index++; index < lines.Length; index++)
			{
				_proseLines[index] = string.Empty;
				var trimmed = lines[index].TrimStart(' ');
				if (trimmed.StartsWith(fence[0].ToString(), StringComparison.Ordinal)
				    && trimmed.TrimEnd().Length >= fence.Length
				    && trimmed.TrimEnd().All(c => c == fence[0]))
				{
					break;
				}

				content.Append(lines[index]).Append('\n');
			}

			codeBlocks.Add(new MarkdownCodeBlock(open.Groups["info"].Value.Trim(), content.ToString(), start + 1));
		}

		CodeBlocks = codeBlocks;
		Anchors = CollectAnchors();
		Links = CollectLinks();
	}

	/// <summary>Repository-relative path of the file.</summary>
	public string RelativePath { get; }

	/// <summary>The fenced code blocks, in order.</summary>
	public IReadOnlyList<MarkdownCodeBlock> CodeBlocks { get; }

	/// <summary>Every anchor a link can target: the GitHub anchor of each heading, and explicit HTML <c>id</c>/<c>name</c> attributes.</summary>
	public IReadOnlySet<string> Anchors { get; }

	/// <summary>Every link and image target outside code: inline links, reference definitions, HTML <c>href</c>/<c>src</c>.</summary>
	public IReadOnlyList<MarkdownLink> Links { get; }

	/// <summary>Loads a repository file.</summary>
	public static MarkdownDocument Load(string relativePath) => new(relativePath, RepositoryFiles.Read(relativePath));

	/// <summary>
	///     The anchor GitHub generates for a heading (github-slugger): the rendered text in lower case, without punctuation
	///     other than <c>-</c> and <c>_</c>, spaces replaced by <c>-</c>. Duplicates get <c>-1</c>, <c>-2</c>, ... (see
	///     <see cref="CollectAnchors" />).
	/// </summary>
	public static string GitHubAnchor(string headingMarkdown)
	{
		var text = MarkdownImage().Replace(headingMarkdown, string.Empty);
		text = MarkdownLinkText().Replace(text, "${text}");
		text = HtmlTag().Replace(text, string.Empty);
		text = text.Replace("`", string.Empty, StringComparison.Ordinal).Replace("*", string.Empty, StringComparison.Ordinal);

		var anchor = new StringBuilder(text.Length);
		foreach (var character in text.Trim().ToLowerInvariant())
		{
			var category = CharUnicodeInfo.GetUnicodeCategory(character);
			if (character == ' ')
			{
				anchor.Append('-');
			}
			else if (char.IsLetterOrDigit(character) || character is '-' or '_'
			         || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation)
			{
				anchor.Append(character);
			}
		}

		return anchor.ToString();
	}

	/// <summary>
	///     The cells of the first table after the heading with this exact text (without the <c>#</c> marks), header and
	///     separator rows excluded. Each cell is trimmed.
	/// </summary>
	public IReadOnlyList<IReadOnlyList<string>> TableRowsAfterHeading(string heading)
	{
		var headingIndex = Array.FindIndex(_proseLines, line => Heading().Match(line) is { Success: true } match && match.Groups["text"].Value == heading);
		if (headingIndex < 0)
		{
			throw new InvalidOperationException($"{RelativePath}: heading '{heading}' not found.");
		}

		var rows = new List<IReadOnlyList<string>>();
		var index = headingIndex + 1;
		while (index < _proseLines.Length && !_proseLines[index].TrimStart().StartsWith('|'))
		{
			if (Heading().IsMatch(_proseLines[index]))
			{
				return rows;
			}

			index++;
		}

		// Skip the header row and the separator row.
		for (index += 2; index < _proseLines.Length && _proseLines[index].TrimStart().StartsWith('|'); index++)
		{
			var cells = UnescapedPipe().Split(_proseLines[index].Trim().Trim('|'));
			rows.Add(cells.Select(cell => cell.Trim()).ToList());
		}

		return rows;
	}

	/// <summary>The prose (outside code blocks) of the section under a heading, up to the next heading of any level.</summary>
	public string SectionText(string heading)
	{
		var headingIndex = Array.FindIndex(_proseLines, line => Heading().Match(line) is { Success: true } match && match.Groups["text"].Value == heading);
		if (headingIndex < 0)
		{
			throw new InvalidOperationException($"{RelativePath}: heading '{heading}' not found.");
		}

		var section = _proseLines.Skip(headingIndex + 1).TakeWhile(line => !Heading().IsMatch(line));
		return string.Join('\n', section);
	}

	private HashSet<string> CollectAnchors()
	{
		var anchors = new HashSet<string>(StringComparer.Ordinal);
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var line in _proseLines)
		{
			var heading = Heading().Match(line);
			if (heading.Success)
			{
				var anchor = GitHubAnchor(heading.Groups["text"].Value);
				anchors.Add(counts.TryGetValue(anchor, out var seen) ? $"{anchor}-{seen}" : anchor);
				counts[anchor] = seen + 1;
			}

			foreach (Match id in HtmlAnchor().Matches(line))
			{
				anchors.Add(id.Groups["id"].Value);
			}
		}

		return anchors;
	}

	private List<MarkdownLink> CollectLinks()
	{
		var links = new List<MarkdownLink>();
		for (var index = 0; index < _proseLines.Length; index++)
		{
			// Link syntax inside a code span is text, not a link.
			var line = InlineCode().Replace(_proseLines[index], match => new string(' ', match.Length));
			foreach (var regex in new[] { InlineLinkTarget(), ReferenceDefinition(), HtmlLinkTarget() })
			{
				foreach (Match match in regex.Matches(line))
				{
					links.Add(new MarkdownLink(match.Groups["target"].Value, index + 1));
				}
			}
		}

		return links;
	}

	[GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})(?<info>[^`]*)$")]
	private static partial Regex FenceOpen();

	[GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$")]
	private static partial Regex Heading();

	[GeneratedRegex(@"<a\s+(?:[^>]*\s)?(?:id|name)\s*=\s*""(?<id>[^""]+)""", RegexOptions.IgnoreCase)]
	private static partial Regex HtmlAnchor();

	[GeneratedRegex(@"(`+).+?\1")]
	private static partial Regex InlineCode();

	/// <summary>The target of every <c>](target)</c>, which also covers images and a badge image nested in a link.</summary>
	[GeneratedRegex(@"\]\(\s*(?:<(?<target>[^>]*)>|(?<target>[^)\s]+))(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
	private static partial Regex InlineLinkTarget();

	[GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*<?(?<target>[^\s>]+)>?")]
	private static partial Regex ReferenceDefinition();

	[GeneratedRegex(@"\b(?:href|src)\s*=\s*""(?<target>[^""]*)""", RegexOptions.IgnoreCase)]
	private static partial Regex HtmlLinkTarget();

	[GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
	private static partial Regex MarkdownImage();

	[GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)")]
	private static partial Regex MarkdownLinkText();

	[GeneratedRegex(@"<[^>]+>")]
	private static partial Regex HtmlTag();

	[GeneratedRegex(@"(?<!\\)\|")]
	private static partial Regex UnescapedPipe();
}
