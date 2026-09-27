using System.Text;

namespace Umbraco.Community.PropertyVisibility.SchemaGenerator;

/// <summary>The summary <c>--check</c> prints for a committed schema that no longer matches the model.</summary>
internal static class DriftReport
{
	private const int MaxListedLines = 40;
	private const long MaxDiffCells = 4_000_000;

	/// <summary>Compares a committed file with the generated content.</summary>
	/// <param name="fileName">Name used in the report.</param>
	/// <param name="committed">The committed text with LF line endings, or <c>null</c> when the file is missing.</param>
	/// <param name="generated">The generated text.</param>
	/// <returns><c>null</c> when both are equal, else a multi-line report.</returns>
	public static string? Create(string fileName, string? committed, string generated)
	{
		if (committed is null)
		{
			return $"{fileName}: missing, the generated schema has {SplitLines(generated).Length} lines";
		}

		if (committed == generated)
		{
			return null;
		}

		if (committed.TrimEnd('\n') == generated.TrimEnd('\n'))
		{
			return $"{fileName}: differs only in its final newline (the generated file ends with exactly one)";
		}

		var before = SplitLines(committed);
		var after = SplitLines(generated);
		var edits = (long)before.Length * after.Length <= MaxDiffCells ? Diff(before, after) : FirstDifference(before, after);

		var removed = edits.Count(e => e.Kind == '-');
		var added = edits.Count(e => e.Kind == '+');
		var report = new StringBuilder()
			.Append(fileName)
			.Append(": differs from the generated output (")
			.Append(removed).Append(" committed line(s) removed, ")
			.Append(added).Append(" generated line(s) added)");

		foreach (var edit in edits.Take(MaxListedLines))
		{
			report.AppendLine().Append("  ").Append(edit.Kind).Append(' ').Append(edit.LineNumber.ToString().PadLeft(4)).Append(": ").Append(edit.Text);
		}

		if (edits.Count > MaxListedLines)
		{
			report.AppendLine().Append("  ... ").Append(edits.Count - MaxListedLines).Append(" more changed line(s)");
		}

		return report.ToString();
	}

	private static string[] SplitLines(string text) => text.TrimEnd('\n').Split('\n');

	/// <summary>Line diff from a longest common subsequence; '-' numbers lines in the committed file, '+' in the generated one.</summary>
	private static List<Edit> Diff(string[] before, string[] after)
	{
		var lcs = new int[before.Length + 1, after.Length + 1];
		for (var i = before.Length - 1; i >= 0; i--)
		{
			for (var j = after.Length - 1; j >= 0; j--)
			{
				lcs[i, j] = before[i] == after[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
			}
		}

		var edits = new List<Edit>();
		int x = 0, y = 0;
		while (x < before.Length || y < after.Length)
		{
			if (x < before.Length && y < after.Length && before[x] == after[y])
			{
				x++;
				y++;
			}
			else if (x < before.Length && (y == after.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
			{
				edits.Add(new Edit('-', x + 1, before[x]));
				x++;
			}
			else
			{
				edits.Add(new Edit('+', y + 1, after[y]));
				y++;
			}
		}

		return edits;
	}

	/// <summary>Fallback for very large inputs: the first differing line of each side.</summary>
	private static List<Edit> FirstDifference(string[] before, string[] after)
	{
		var index = 0;
		while (index < before.Length && index < after.Length && before[index] == after[index])
		{
			index++;
		}

		var edits = new List<Edit>();
		if (index < before.Length)
		{
			edits.Add(new Edit('-', index + 1, before[index]));
		}

		if (index < after.Length)
		{
			edits.Add(new Edit('+', index + 1, after[index]));
		}

		return edits;
	}

	private sealed record Edit(char Kind, int LineNumber, string Text);
}
