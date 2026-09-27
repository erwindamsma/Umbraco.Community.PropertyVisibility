namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     Files of the repository the tests run from (the folder holding <c>Umbraco.Community.PropertyVisibility.slnx</c>),
///     addressed by repository-relative paths with forward slashes.
/// </summary>
internal static class RepositoryFiles
{
	private const string SolutionFile = "Umbraco.Community.PropertyVisibility.slnx";

	/// <summary>Folders whose Markdown files are documentation (the repository root itself is scanned without subfolders).</summary>
	private static readonly string[] DocumentationFolders = ["docs", ".github"];

	private static readonly Lazy<string> RootPath = new(FindRoot);

	/// <summary>Absolute path of the repository root.</summary>
	public static string Root => RootPath.Value;

	/// <summary>Absolute path of a repository-relative path.</summary>
	public static string PathOf(string relativePath) =>
		Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

	/// <summary>Reads a repository file.</summary>
	public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

	/// <summary>
	///     The documentation Markdown files: <c>*.md</c> in the repository root, and every <c>*.md</c> under <c>docs</c> and
	///     <c>.github</c>. Repository-relative, forward slashes, sorted.
	/// </summary>
	public static IReadOnlyList<string> MarkdownFiles()
	{
		var files = Directory.EnumerateFiles(Root, "*.md", SearchOption.TopDirectoryOnly)
			.Concat(DocumentationFolders
				.Select(PathOf)
				.Where(Directory.Exists)
				.SelectMany(folder => Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)));

		return files.Select(ToRelative).Order(StringComparer.Ordinal).ToList();
	}

	/// <summary>Repository-relative path (forward slashes) of an absolute path inside the repository.</summary>
	public static string ToRelative(string absolutePath) =>
		Path.GetRelativePath(Root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

	/// <summary>
	///     Whether a repository-relative file or folder exists with exactly this spelling. GitHub, NuGet and Linux compare
	///     paths case-sensitively, so a link that only works on a case-insensitive file system counts as broken.
	/// </summary>
	public static bool ExistsWithExactCase(string relativePath, bool allowDirectory = true)
	{
		var current = Root;
		var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		for (var index = 0; index < segments.Length; index++)
		{
			if (!Directory.Exists(current))
			{
				return false;
			}

			var segment = segments[index];
			var match = Directory.EnumerateFileSystemEntries(current)
				.Select(Path.GetFileName)
				.FirstOrDefault(name => string.Equals(name, segment, StringComparison.Ordinal));
			if (match is null)
			{
				return false;
			}

			current = Path.Combine(current, match);
		}

		return File.Exists(current) || (allowDirectory && Directory.Exists(current));
	}

	private static string FindRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException($"Repository root ({SolutionFile}) not found above the test output folder.");
	}
}
