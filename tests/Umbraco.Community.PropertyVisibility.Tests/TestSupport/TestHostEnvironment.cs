using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     A content root in a fresh temporary folder, served by a real <see cref="PhysicalFileProvider" /> (so file watching
///     behaves as in a site). <see cref="WatchFailure" /> makes starting a watch fail, as it does when the folder cannot be
///     read or the system's file watcher limit is reached. Disposing stops the watcher and deletes the folder.
/// </summary>
internal sealed class TestHostEnvironment : IHostEnvironment, IDisposable
{
	private readonly PhysicalFileProvider _fileProvider;
	private readonly FailingWatchFileProvider _contentRootFileProvider;

	/// <summary>Creates the folder and the file provider.</summary>
	public TestHostEnvironment()
	{
		ContentRootPath = TemporaryFolder.Create();
		_fileProvider = new PhysicalFileProvider(ContentRootPath);
		_contentRootFileProvider = new FailingWatchFileProvider(_fileProvider);
	}

	/// <inheritdoc />
	public string EnvironmentName { get; set; } = Environments.Development;

	/// <inheritdoc />
	public string ApplicationName { get; set; } = "Umbraco.Community.PropertyVisibility.Tests";

	/// <inheritdoc />
	public string ContentRootPath { get; set; }

	/// <inheritdoc />
	public IFileProvider ContentRootFileProvider
	{
		get => _contentRootFileProvider;
		set => throw new NotSupportedException();
	}

	/// <summary>
	///     When set, <see cref="IFileProvider.Watch" /> on the content root throws it (for the next and every later watch,
	///     including the re-arm after a change). <c>null</c> watches normally.
	/// </summary>
	public Exception? WatchFailure
	{
		get => _contentRootFileProvider.WatchFailure;
		set => _contentRootFileProvider.WatchFailure = value;
	}

	/// <summary>Writes a file under the content root (UTF-8 without byte order mark).</summary>
	/// <param name="relativePath">Path relative to the content root.</param>
	/// <param name="content">The file content.</param>
	/// <returns>The full path.</returns>
	public string WriteFile(string relativePath, string content)
	{
		var path = Path.Combine(ContentRootPath, relativePath);
		File.WriteAllText(path, content);
		return path;
	}

	/// <summary>Deletes a file under the content root, when it exists.</summary>
	/// <param name="relativePath">Path relative to the content root.</param>
	public void DeleteFile(string relativePath) => File.Delete(Path.Combine(ContentRootPath, relativePath));

	/// <inheritdoc />
	public void Dispose()
	{
		_fileProvider.Dispose();
		TemporaryFolder.Delete(ContentRootPath);
	}

	private sealed class FailingWatchFileProvider(IFileProvider inner) : IFileProvider
	{
		private Exception? _watchFailure;

		public Exception? WatchFailure
		{
			get => Volatile.Read(ref _watchFailure);
			set => Volatile.Write(ref _watchFailure, value);
		}

		public IDirectoryContents GetDirectoryContents(string subpath) => inner.GetDirectoryContents(subpath);

		public IFileInfo GetFileInfo(string subpath) => inner.GetFileInfo(subpath);

		public IChangeToken Watch(string filter) => WatchFailure is { } failure ? throw failure : inner.Watch(filter);
	}
}

/// <summary>
///     Creates and removes uniquely named folders under the system temp folder.
/// </summary>
internal static class TemporaryFolder
{
	/// <summary>Creates a new, empty folder.</summary>
	/// <returns>Its full path.</returns>
	public static string Create()
	{
		var path = Path.Combine(Path.GetTempPath(), "pv-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>
	///     Deletes the folder and its content. A file watcher that is still shutting down can hold the folder for a moment,
	///     so a few attempts are made; a folder that cannot be deleted is left for the OS temp cleanup.
	/// </summary>
	/// <param name="path">The folder.</param>
	public static void Delete(string path)
	{
		for (var attempt = 0; attempt < 5; attempt++)
		{
			try
			{
				if (Directory.Exists(path))
				{
					Directory.Delete(path, recursive: true);
				}

				return;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(100);
			}
		}
	}
}
