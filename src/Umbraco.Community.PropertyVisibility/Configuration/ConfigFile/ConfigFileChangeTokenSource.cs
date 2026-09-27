using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Tells the options monitor to rebuild <see cref="PropertyVisibilityOptions" /> when the rules file changes, so an
///     edit takes effect without a restart.
/// </summary>
/// <remarks>
///     <para>
///         Watches <see cref="PropertyVisibilityOptions.ConfigFile" /> (read from the <c>PropertyVisibility</c> section of
///         the configuration, <see cref="PropertyVisibilityOptions.DefaultConfigFile" /> when absent) through
///         <see cref="IHostEnvironment.ContentRootFileProvider" />. A path outside the content root (absolute, or with
///         <c>..</c>) gets its own <see cref="PhysicalFileProvider" /> on the file's folder, which must exist when the watch
///         starts, and so does a file inside it whose name starts with <c>.</c> or that is hidden or a system file: the
///         content root's provider ignores the events of such files (<c>ExclusionFilters.Sensitive</c>), the own provider
///         is created without exclusions. The attributes are checked when the watch starts and on every configuration
///         reload. Creating, changing and deleting the file all count. When
///         appsettings changes the file name, the watch moves to the new file; the appsettings change itself rebuilds the
///         options through the binder's own token.
///     </para>
///     <para>
///         Editors write a file in bursts (truncate, write, touch), so file events restart a 250 ms timer and the change
///         token fires once, when the timer elapses after the last event of the burst. The same timer serves the retries
///         of <see cref="ConfigFileReadRetry" /> for a file that was still locked when it was read. File watching relies on
///         <see cref="PhysicalFileProvider" />: on Docker volumes and network shares set
///         <c>DOTNET_USE_POLLING_FILE_WATCHER=1</c>.
///     </para>
///     <para>
///         Never throws, from the constructor or from a change callback: the options monitor, and everything that depends
///         on it, is built from this instance. A watch that cannot start or cannot be re-armed (the folder is missing or
///         unreadable, the system's file watcher limit is reached) leaves the file unwatched: one warning is logged and the
///         health check shows <see cref="IssueCodes.ConfigFileNotWatched" /> until the next appsettings reload starts the
///         watch again or turns the file source off. The rules file is still read on every rebuild.
///     </para>
/// </remarks>
public sealed class ConfigFileChangeTokenSource : IOptionsChangeTokenSource<PropertyVisibilityOptions>, IDisposable
{
	/// <summary>
	///     Quiet time after the last file event before the options are rebuilt.
	/// </summary>
	public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

	private const string ConfigFilePath = $"{PropertyVisibilityOptions.SectionName}:{nameof(PropertyVisibilityOptions.ConfigFile)}";

	private readonly object _gate = new();
	private readonly IHostEnvironment _hostEnvironment;
	private readonly IConfigurationSection _section;
	private readonly ConfigurationInfo _info;
	private readonly ConfigFileReadRetry _readRetry;
	private readonly ILogger<ConfigFileChangeTokenSource> _logger;
	private readonly TimeSpan _debounce;
	private readonly Timer _debounceTimer;
	private readonly Action<TimeSpan> _scheduleReload;
	private readonly IDisposable _configurationSubscription;

	// Guarded by _gate.
	private CancellationTokenSource _changeSource = new();
	private WatchAttempt? _attempt;
	private string? _watchedPath;
	private bool _watchedWithOwnProvider;
	private IDisposable? _fileSubscription;
	private PhysicalFileProvider? _ownProvider;
	private string? _lastLoggedWatchFailure;
	private bool _disposed;

	/// <summary>
	///     Initializes a new instance of the <see cref="ConfigFileChangeTokenSource" /> class and starts watching.
	/// </summary>
	/// <param name="hostEnvironment">Supplies the content root and its file provider.</param>
	/// <param name="configuration">The configuration the options are bound from (its <c>PropertyVisibility</c> section is read).</param>
	/// <param name="info">Receives the watch problem (<see cref="IssueCodes.ConfigFileNotWatched" />) for the health check.</param>
	/// <param name="readRetry">Asks for a rebuild when a locked rules file is due to be read again.</param>
	/// <param name="logger">The logger.</param>
	internal ConfigFileChangeTokenSource(
		IHostEnvironment hostEnvironment,
		IConfiguration configuration,
		ConfigurationInfo info,
		ConfigFileReadRetry readRetry,
		ILogger<ConfigFileChangeTokenSource> logger)
		: this(hostEnvironment, configuration, info, readRetry, logger, Debounce)
	{
	}

	/// <summary>
	///     Initializes a new instance with a custom debounce, for tests.
	/// </summary>
	internal ConfigFileChangeTokenSource(
		IHostEnvironment hostEnvironment,
		IConfiguration configuration,
		ConfigurationInfo info,
		ConfigFileReadRetry readRetry,
		ILogger<ConfigFileChangeTokenSource> logger,
		TimeSpan debounce)
	{
		ArgumentNullException.ThrowIfNull(hostEnvironment);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(readRetry);
		ArgumentNullException.ThrowIfNull(logger);

		_hostEnvironment = hostEnvironment;
		_section = configuration.GetSection(PropertyVisibilityOptions.SectionName);
		_info = info;
		_readRetry = readRetry;
		_logger = logger;
		_debounce = debounce;
		_debounceTimer = new Timer(static state => ((ConfigFileChangeTokenSource)state!).OnDebounceElapsed(), this, Timeout.Infinite, Timeout.Infinite);
		_scheduleReload = ScheduleReload;

		WatchConfiguredFile();
		_configurationSubscription = ChangeToken.OnChange(configuration.GetReloadToken, WatchConfiguredFile);
		_readRetry.Attach(_scheduleReload);
	}

	/// <inheritdoc />
	public string? Name => Options.DefaultName;

	/// <summary>
	///     The full path currently watched; <c>null</c> when the file source is off or the path cannot be watched.
	/// </summary>
	internal string? WatchedPath
	{
		get
		{
			lock (_gate)
			{
				return _watchedPath;
			}
		}
	}

	/// <inheritdoc />
	public IChangeToken GetChangeToken()
	{
		lock (_gate)
		{
			return new CancellationChangeToken(_changeSource.Token);
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		IDisposable? fileSubscription;
		PhysicalFileProvider? ownProvider;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			fileSubscription = _fileSubscription;
			ownProvider = _ownProvider;
			_fileSubscription = null;
			_ownProvider = null;
			_attempt = null;
		}

		// Disposed outside the lock: disposing a registration waits for a callback that may be running and waiting for the lock.
		_readRetry.Detach(_scheduleReload);
		_configurationSubscription.Dispose();
		DisposeQuietly(fileSubscription);
		DisposeQuietly(ownProvider);
		_debounceTimer.Dispose();
	}

	private static bool PathsEqual(string? a, string? b)
		=> string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

	private static bool IsOutside(string relativePath)
		=> Path.IsPathRooted(relativePath)
			|| relativePath == ".."
			|| relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
			|| relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

	private static string TrimSeparator(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static void DisposeQuietly(IDisposable? disposable)
	{
		try
		{
			disposable?.Dispose();
		}
		catch (Exception)
		{
			// Stopping a watcher that already failed can throw again; there is nothing left to release.
		}
	}

	private string? ResolveConfiguredPath()
	{
		var configured = (_section[nameof(PropertyVisibilityOptions.ConfigFile)] ?? PropertyVisibilityOptions.DefaultConfigFile).Trim();
		if (configured.Length == 0)
		{
			return null;
		}

		try
		{
			return Path.GetFullPath(configured, _hostEnvironment.ContentRootPath);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			// ConfigFileOptionsSetup reports the invalid path; there is nothing to watch.
			return null;
		}
	}

	// Called from the constructor and on every configuration reload; never throws (a reload callback that throws would
	// stop the host's other reload listeners, and the constructor must always produce a working instance).
	private void WatchConfiguredFile()
	{
		var toDispose = new List<IDisposable>(3);
		(string Path, Exception Error)? failure = null;
		var watching = false;
		var fileSourceOff = false;
		try
		{
			lock (_gate)
			{
				if (_disposed)
				{
					return;
				}

				// Nothing to do only while the same file is watched the way it needs to be (a file in the content root may have
				// become hidden since). With the file source off (fullPath null) this goes on, so a watch failure reported for
				// an earlier ConfigFile is cleared.
				var fullPath = ResolveConfiguredPath();
				if (fullPath is not null
					&& PathsEqual(fullPath, _watchedPath)
					&& _fileSubscription is not null
					&& NeedsOwnProvider(fullPath) == _watchedWithOwnProvider)
				{
					return;
				}

				if (_fileSubscription is not null)
				{
					toDispose.Add(_fileSubscription);
					_fileSubscription = null;
				}

				_watchedPath = null;
				_attempt = null;
				if (fullPath is null)
				{
					ReleaseOwnProvider(keepDirectory: null, toDispose);
					fileSourceOff = true;
				}
				else
				{
					failure = StartWatch(fullPath, toDispose);
					watching = failure is null;
				}
			}
		}
		catch (Exception ex)
		{
			failure = (ResolveConfiguredPathQuietly() ?? string.Empty, ex);
		}
		finally
		{
			foreach (IDisposable disposable in toDispose)
			{
				DisposeQuietly(disposable);
			}
		}

		if (failure is { } failed)
		{
			ReportWatchFailure(failed.Path, failed.Error);
		}
		else if (watching || fileSourceOff)
		{
			ClearWatchFailure(logRecovery: watching);
		}
	}

	private string? ResolveConfiguredPathQuietly()
	{
		try
		{
			return ResolveConfiguredPath();
		}
		catch (Exception)
		{
			return null;
		}
	}

	// Called under _gate. Returns the failure, or null when the watch is running.
	private (string Path, Exception Error)? StartWatch(string fullPath, List<IDisposable> toDispose)
	{
		var attempt = new WatchAttempt(fullPath);
		try
		{
			Func<IChangeToken> produceToken = CreateTokenProducer(fullPath, toDispose, out var ownProvider);
			_attempt = attempt;

			// ChangeToken.OnChange calls the producer right away (still under _gate; the lock is re-entrant) and again after
			// every change. The safe producer turns a failure into a token that never fires and marks the attempt failed.
			IDisposable subscription = ChangeToken.OnChange(() => ProduceToken(attempt, produceToken), OnFileChanged);
			if (attempt.Error is { } initialError)
			{
				toDispose.Add(subscription);
				_attempt = null;
				ReleaseOwnProvider(keepDirectory: null, toDispose);
				return (fullPath, initialError);
			}

			_watchedPath = fullPath;
			_watchedWithOwnProvider = ownProvider;
			_fileSubscription = subscription;
			return null;
		}
		catch (Exception ex)
		{
			_attempt = null;
			ReleaseOwnProvider(keepDirectory: null, toDispose);
			return (fullPath, ex);
		}
	}

	// Called under _gate. Throws when the watch cannot be set up.
	private Func<IChangeToken> CreateTokenProducer(string fullPath, List<IDisposable> toDispose, out bool ownProvider)
	{
		ownProvider = NeedsOwnProvider(fullPath);
		if (!ownProvider)
		{
			ReleaseOwnProvider(keepDirectory: null, toDispose);
			IFileProvider provider = _hostEnvironment.ContentRootFileProvider;
			var filter = Path.GetRelativePath(_hostEnvironment.ContentRootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
			return () => provider.Watch(filter);
		}

		var directory = Path.GetDirectoryName(fullPath);
		if (directory is null || !Directory.Exists(directory))
		{
			ReleaseOwnProvider(keepDirectory: null, toDispose);
			throw new DirectoryNotFoundException($"The folder '{directory}' does not exist.");
		}

		// No exclusion filter: the default one (ExclusionFilters.Sensitive) drops the events of a dot-prefixed, hidden or
		// system file without a trace.
		ReleaseOwnProvider(keepDirectory: directory, toDispose);
		_ownProvider ??= new PhysicalFileProvider(directory, ExclusionFilters.None);
		PhysicalFileProvider own = _ownProvider;
		var fileName = Path.GetFileName(fullPath);
		return () => own.Watch(fileName);
	}

	// A file outside the content root, or one whose events the content root's provider drops, is watched through an own
	// provider on its folder.
	private bool NeedsOwnProvider(string fullPath)
		=> IsOutside(Path.GetRelativePath(_hostEnvironment.ContentRootPath, fullPath)) || IsExcludedByDefault(fullPath);

	// Whether the content root's provider, created with the default ExclusionFilters.Sensitive, would drop the file's events:
	// a name starting with '.', or an existing file with the Hidden or System attribute (on Linux and macOS, Hidden is the
	// leading '.'). Checked when the watch starts and on every configuration reload; a file that becomes hidden in between
	// is not noticed until then.
	private static bool IsExcludedByDefault(string fullPath)
	{
		if (Path.GetFileName(fullPath).StartsWith('.'))
		{
			return true;
		}

		try
		{
			var file = new FileInfo(fullPath);
			return file.Exists && (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
		{
			return false;
		}
	}

	private IChangeToken ProduceToken(WatchAttempt attempt, Func<IChangeToken> produceToken)
	{
		try
		{
			return produceToken();
		}
		catch (Exception ex)
		{
			OnWatchFailed(attempt, ex);
			return NullChangeToken.Singleton;
		}
	}

	// A producer failed: on the first call inside StartWatch (which cleans up itself), or when re-arming after a change.
	private void OnWatchFailed(WatchAttempt attempt, Exception error)
	{
		IDisposable? subscription;
		PhysicalFileProvider? ownProvider;
		lock (_gate)
		{
			attempt.Error = error;
			if (_disposed || !ReferenceEquals(_attempt, attempt) || _fileSubscription is null)
			{
				return;
			}

			// Re-arming failed: the watch is dead. Forget it, so the next configuration reload starts a new one.
			_attempt = null;
			_watchedPath = null;
			subscription = _fileSubscription;
			_fileSubscription = null;
			ownProvider = _ownProvider;
			_ownProvider = null;
		}

		// This runs inside the dying registration's own callback; release it on another thread.
		ThreadPool.QueueUserWorkItem(
			static released =>
			{
				DisposeQuietly(released.Subscription);
				DisposeQuietly(released.Provider);
			},
			(Subscription: subscription, Provider: ownProvider),
			preferLocal: false);

		ReportWatchFailure(attempt.FullPath, error);
	}

	// Called under _gate. Keeps the own provider only when it already serves keepDirectory.
	private void ReleaseOwnProvider(string? keepDirectory, List<IDisposable> toDispose)
	{
		if (_ownProvider is null || (keepDirectory is not null && PathsEqual(TrimSeparator(_ownProvider.Root), TrimSeparator(keepDirectory))))
		{
			return;
		}

		toDispose.Add(_ownProvider);
		_ownProvider = null;
	}

	private void ReportWatchFailure(string fullPath, Exception error)
	{
		var issue = new ConfigurationIssue(
			IssueCodes.ConfigFileNotWatched,
			IssueSeverity.Warning,
			$"The rules file '{fullPath}' is not watched ({error.Message.TrimEnd('.')}): edits to it take effect after a restart or an appsettings change. On Docker volumes and network shares, or when the system's file watcher limit is reached, set the environment variable DOTNET_USE_POLLING_FILE_WATCHER=1.",
			ConfigFilePath);

		bool log;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			var signature = $"{fullPath}|{error.GetType().FullName}|{error.Message}";
			log = !string.Equals(_lastLoggedWatchFailure, signature, StringComparison.Ordinal);
			_lastLoggedWatchFailure = signature;
			_info.SetWatchIssue(issue);
		}

		if (log)
		{
			_logger.LogWarning(
				error,
				"{IssueCode}: the PropertyVisibility rules file {ConfigFile} is not watched; edits to it take effect after a restart or an appsettings change. On Docker volumes and network shares, or when the system's file watcher limit is reached, set DOTNET_USE_POLLING_FILE_WATCHER=1.",
				IssueCodes.ConfigFileNotWatched,
				fullPath);
		}
	}

	private void ClearWatchFailure(bool logRecovery)
	{
		string? path;
		lock (_gate)
		{
			if (_lastLoggedWatchFailure is null && _info.WatchIssue is null)
			{
				return;
			}

			_lastLoggedWatchFailure = null;
			_info.SetWatchIssue(null);
			path = _watchedPath;
		}

		if (logRecovery && path is not null)
		{
			_logger.LogInformation("PropertyVisibility: the rules file {ConfigFile} is watched again.", path);
		}
	}

	private void OnFileChanged()
	{
		lock (_gate)
		{
			if (!_disposed)
			{
				_debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
			}
		}
	}

	// ConfigFileReadRetry: the file was locked when it was read; rebuild again after the delay. Only arms the timer, so
	// the options are never rebuilt from inside the build that asked for the retry.
	private void ScheduleReload(TimeSpan delay)
	{
		lock (_gate)
		{
			if (!_disposed)
			{
				_debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
			}
		}
	}

	private void OnDebounceElapsed()
	{
		CancellationTokenSource fired;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			fired = _changeSource;
			_changeSource = new CancellationTokenSource();
		}

		try
		{
			// The options monitor rebuilds the options inside this call and asks for the next token (already swapped in).
			fired.Cancel();
		}
		catch (Exception)
		{
			// A rebuild that fails (invalid options) throws out of the monitor's listener. The monitor caches that failure,
			// the service logs it on its next read (PV008), and an exception must not escape a timer callback.
		}
	}

	/// <summary>One start of the watch; a failed re-arm only tears down the attempt it belongs to.</summary>
	private sealed class WatchAttempt
	{
		public WatchAttempt(string fullPath) => FullPath = fullPath;

		public string FullPath { get; }

		// Guarded by the owner's _gate.
		public Exception? Error { get; set; }
	}
}
