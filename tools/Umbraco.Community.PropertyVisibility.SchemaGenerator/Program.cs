using Umbraco.Community.PropertyVisibility.SchemaGenerator;

// Writes (or, with --check, compares) the JSON schemas that ship in the package. Exit codes: 0 success or no drift,
// 1 drift found by --check, 2 usage error.

const string Usage = """
	Usage: dotnet run --project tools/Umbraco.Community.PropertyVisibility.SchemaGenerator -- --out <dir> [--check]

	  --out <dir>  Folder that holds the schemas (the package project: src/Umbraco.Community.PropertyVisibility).
	  --check      Do not write; exit 1 and print a diff summary when a file in <dir> differs from the generated output.
	""";

string? outDir = null;
var check = false;

for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "--out" when i + 1 < args.Length:
			outDir = args[++i];
			break;
		case "--check":
			check = true;
			break;
		case "-h" or "--help" or "-?":
			Console.WriteLine(Usage);
			return 0;
		default:
			Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.");
			Console.Error.WriteLine(Usage);
			return 2;
	}
}

if (string.IsNullOrWhiteSpace(outDir))
{
	Console.Error.WriteLine("--out <dir> is required.");
	Console.Error.WriteLine(Usage);
	return 2;
}

// The descriptions come from the package's XML documentation file; without it the schemas would silently lose them.
var documentationFile = SchemaFiles.DocumentationFile;
if (!File.Exists(documentationFile))
{
	Console.Error.WriteLine($"XML documentation file '{documentationFile}' not found; build the package project with GenerateDocumentationFile.");
	return 2;
}

var directory = Path.GetFullPath(outDir);
var files = SchemaFiles.Generate();

if (check)
{
	if (!Directory.Exists(directory))
	{
		Console.Error.WriteLine($"--out folder '{directory}' does not exist.");
		return 2;
	}

	var drifted = 0;
	foreach (var file in files)
	{
		var path = Path.Combine(directory, file.FileName);
		var report = DriftReport.Create(file.FileName, TextFile.ReadOrNull(path), file.Content);
		if (report is null)
		{
			Console.WriteLine($"{file.FileName}: up to date");
			continue;
		}

		drifted++;
		Console.WriteLine(report);
	}

	if (drifted == 0)
	{
		return 0;
	}

	Console.WriteLine();
	Console.WriteLine($"{drifted} schema file(s) differ from the options model. Regenerate and commit them:");
	Console.WriteLine("  dotnet run --project tools/Umbraco.Community.PropertyVisibility.SchemaGenerator -- --out src/Umbraco.Community.PropertyVisibility");
	return 1;
}

Directory.CreateDirectory(directory);
foreach (var file in files)
{
	var path = Path.Combine(directory, file.FileName);
	if (TextFile.ReadOrNull(path) == file.Content)
	{
		Console.WriteLine($"{file.FileName}: unchanged");
		continue;
	}

	TextFile.Write(path, file.Content);
	Console.WriteLine($"{file.FileName}: written");
}

return 0;
