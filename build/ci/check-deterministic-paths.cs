// Checks that the assemblies in the packages of a folder were built with deterministic source paths
// (ContinuousIntegrationBuild): the PDB path in the CodeView entry is a bare file name or starts with /_/, every
// document of the embedded portable PDB starts with /_/ (or /_1/ and so on for other source roots), and no file in the
// package contains the repository root or the home folder of the machine that built it. The embedded PDB is compressed
// and stores document names in parts, so a plain grep of the dll cannot tell; this reads it with
// System.Reflection.Metadata.
//
// Usage (a .NET 10 file-based app, run by build/ci/pack.sh in CI builds):
//   dotnet run build/ci/check-deterministic-paths.cs -- <folder with *.nupkg> <repository root>

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length != 2)
{
	Console.Error.WriteLine("Usage: dotnet run build/ci/check-deterministic-paths.cs -- <folder with *.nupkg> <repository root>");
	return 2;
}

string folder = args[0];
var needles = MachinePaths(args[1], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
var mappedPath = new Regex(@"^/_\d*/", RegexOptions.CultureInvariant);
var failures = new List<string>();

string[] packages = Directory.GetFiles(folder, "*.nupkg");
if (packages.Length == 0)
{
	Console.Error.WriteLine($"No .nupkg in {folder}");
	return 1;
}

foreach (string package in packages)
{
	Console.WriteLine($"== {Path.GetFileName(package)}");
	using ZipArchive zip = ZipFile.OpenRead(package);
	int assemblies = 0;
	foreach (ZipArchiveEntry entry in zip.Entries)
	{
		byte[] content = Read(entry);
		ReportMachinePaths(entry.FullName, content);
		if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		{
			assemblies++;
			CheckAssembly(entry.FullName, content);
		}
	}
	if (assemblies == 0)
	{
		failures.Add($"{Path.GetFileName(package)} contains no assembly");
	}
}

if (failures.Count > 0)
{
	foreach (string failure in failures)
	{
		Console.Error.WriteLine($"FAIL: {failure}");
	}
	return 1;
}

Console.WriteLine("Deterministic source paths: every PDB document is under /_/, no build machine path in the package");
return 0;

void CheckAssembly(string name, byte[] content)
{
	using var pe = new PEReader(new MemoryStream(content));
	var debugEntries = pe.ReadDebugDirectory();

	var codeViews = debugEntries.Where(e => e.Type == DebugDirectoryEntryType.CodeView).ToList();
	if (codeViews.Count == 0)
	{
		failures.Add($"{name} has no CodeView debug entry");
	}
	foreach (var codeView in codeViews)
	{
		string pdbPath = pe.ReadCodeViewDebugDirectoryData(codeView).Path;
		// An embedded PDB is named by its file name only; a separate one by its (mapped) path.
		bool fileNameOnly = pdbPath.IndexOfAny(['/', '\\']) < 0;
		if (!fileNameOnly && !mappedPath.IsMatch(pdbPath))
		{
			failures.Add($"{name}: CodeView PDB path {pdbPath} is not mapped to /_/");
		}
		else
		{
			Console.WriteLine($"{name}: CodeView PDB path {pdbPath}");
		}
	}

	var embedded = debugEntries.Where(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).ToList();
	if (embedded.Count == 0)
	{
		failures.Add($"{name} has no embedded portable PDB (DebugType embedded)");
		return;
	}

	// Entry data: "MPDB", the uncompressed size (int32), then the deflated portable PDB.
	byte[] pdbImage = InflateEmbeddedPdb(content.AsSpan(embedded[0].DataPointer, embedded[0].DataSize));
	// The decompressed PDB also holds the source link map and the compilation options.
	ReportMachinePaths($"{name} (embedded PDB)", pdbImage);

	using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdbImage));
	MetadataReader pdb = provider.GetMetadataReader();
	int documents = 0;
	var unmapped = new List<string>();
	foreach (DocumentHandle handle in pdb.Documents)
	{
		documents++;
		string document = pdb.GetString(pdb.GetDocument(handle).Name);
		if (!mappedPath.IsMatch(document))
		{
			unmapped.Add(document);
		}
	}
	if (documents == 0)
	{
		failures.Add($"{name}: the embedded PDB lists no documents");
	}
	foreach (string document in unmapped)
	{
		failures.Add($"{name}: embedded PDB document {document} is not mapped to /_/");
	}
	if (unmapped.Count == 0 && documents > 0)
	{
		Console.WriteLine($"{name}: {documents} embedded PDB documents, all under /_/");
	}
}

static byte[] InflateEmbeddedPdb(ReadOnlySpan<byte> data)
{
	if (data.Length < 8 || !data[..4].SequenceEqual("MPDB"u8))
	{
		throw new InvalidDataException("The embedded PDB entry does not start with MPDB");
	}
	int size = BitConverter.ToInt32(data[4..8]);
	using var deflate = new DeflateStream(new MemoryStream(data[8..].ToArray()), CompressionMode.Decompress);
	byte[] image = new byte[size];
	deflate.ReadExactly(image);
	return image;
}

void ReportMachinePaths(string name, byte[] content)
{
	foreach ((string text, byte[] bytes) in needles)
	{
		if (content.AsSpan().IndexOf(bytes) >= 0)
		{
			failures.Add($"{name} contains the build machine path {text}");
		}
	}
}

static byte[] Read(ZipArchiveEntry entry)
{
	using Stream stream = entry.Open();
	using var buffer = new MemoryStream();
	stream.CopyTo(buffer);
	return buffer.ToArray();
}

// The repository root and home folder in the spellings a build can write them: both separators, UTF-8 and UTF-16.
static List<(string Text, byte[] Bytes)> MachinePaths(params string[] paths)
{
	var result = new List<(string, byte[])>();
	foreach (string path in paths)
	{
		string trimmed = path.TrimEnd('/', '\\');
		// A root such as / or C:\ would match everything.
		if (trimmed.Length < 4)
		{
			continue;
		}
		foreach (string spelling in new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace('/', '\\') }.Distinct())
		{
			result.Add((spelling, Encoding.UTF8.GetBytes(spelling)));
			result.Add((spelling, Encoding.Unicode.GetBytes(spelling)));
		}
	}
	return result;
}
