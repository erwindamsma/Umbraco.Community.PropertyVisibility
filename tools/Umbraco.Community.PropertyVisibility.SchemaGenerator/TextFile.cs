using System.Text;

namespace Umbraco.Community.PropertyVisibility.SchemaGenerator;

/// <summary>Text normalization shared by writing and checking, so both see the same text on every platform.</summary>
internal static class TextFile
{
	private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

	/// <summary>
	///     Converts CRLF and CR to LF. A committed file checked out with CRLF (Windows without the repository's
	///     .gitattributes) therefore still compares equal.
	/// </summary>
	/// <param name="text">Any text.</param>
	/// <returns>The text with LF line endings.</returns>
	public static string ToLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

	/// <summary>The form every generated file is written in: LF line endings and exactly one final newline.</summary>
	/// <param name="text">Generated text.</param>
	/// <returns>The normalized text.</returns>
	public static string Normalize(string text) => ToLf(text).TrimEnd('\n') + "\n";

	/// <summary>Reads a file as text with LF line endings (a UTF-8 byte order mark is dropped).</summary>
	/// <param name="path">The file.</param>
	/// <returns>The text, or <c>null</c> when the file does not exist.</returns>
	public static string? ReadOrNull(string path) => File.Exists(path) ? ToLf(File.ReadAllText(path)) : null;

	/// <summary>Writes UTF-8 without a byte order mark.</summary>
	/// <param name="path">Target file.</param>
	/// <param name="content">Normalized content.</param>
	public static void Write(string path, string content) => File.WriteAllText(path, content, Utf8WithoutBom);
}
