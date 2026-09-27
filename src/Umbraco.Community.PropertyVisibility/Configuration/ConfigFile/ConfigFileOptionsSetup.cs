using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Applies the rules file (<see cref="PropertyVisibilityOptions.ConfigFile" />) on top of the options bound from
///     appsettings, and records the outcome in <see cref="ConfigurationInfo" />.
/// </summary>
/// <remarks>
///     <para>
///         Registered after the appsettings binding, so it sees the bound values. <see cref="PropertyVisibilityOptions.ConfigFile" />
///         is resolved against <see cref="IHostEnvironment.ContentRootPath" /> (an absolute path is used as is). An empty
///         value turns the file source off. A missing file, or one holding no JSON value (empty or only comments), is not
///         an error: the appsettings rules apply. A file that parses replaces <see cref="PropertyVisibilityOptions.ContentTypes" />
///         and <see cref="PropertyVisibilityOptions.Sites" /> wholesale and overrides
///         <see cref="PropertyVisibilityOptions.HideEmptiedContainers" /> only when it sets it; <see cref="PropertyVisibilityOptions.Enabled" />
///         and <see cref="PropertyVisibilityOptions.ConfigFile" /> stay appsettings-only. UTF-8 is expected; a file with a
///         UTF-16 or UTF-32 byte order mark is transcoded.
///     </para>
///     <para>
///         A file that does not parse (<see cref="IssueCodes.InvalidJson" />, <see cref="IssueCodes.UnknownKey" />), is
///         larger than <see cref="MaxFileSizeBytes" /> or cannot be opened keeps the last valid version of the same file in
///         effect: the last version that parsed and passed <see cref="PropertyVisibilityOptionsValidator" />. Without one it
///         contributes no rules, so the appsettings rules stay replaced and nothing is hidden by rules until the file is
///         fixed. A file that cannot be opened because it is still locked by the program that wrote it is read again after
///         a backoff (<see cref="ConfigFileReadRetry" />), because releasing a lock raises no file event. Denied access and a
///         path that is too long are not retried: the file is read again on the next change of the file or of appsettings
///         (or a restart). The size limit is enforced while reading too, so a device or <c>/proc</c> file, whose reported
///         length is 0, cannot be read without end. Appsettings rules next to an
///         active file raise <see cref="IssueCodes.BothSourcesDefineRules" />. Each distinct failure and each new warning is
///         logged once, however often the options are rebuilt.
///     </para>
///     <para>
///         Runs on the options monitor's rebuild path, possibly on several threads at once (a file change and an appsettings
///         change, or <c>IOptions</c> and <c>IOptionsMonitor</c> building side by side). One lock serializes the load, the
///         last-good cache, the log deduplication and the <see cref="ConfigurationInfo" /> update. Never throws: an
///         unexpected failure is reported as <see cref="IssueCodes.InvalidJson" /> and handled like a parse failure.
///     </para>
/// </remarks>
public sealed class ConfigFileOptionsSetup : IConfigureOptions<PropertyVisibilityOptions>
{
	/// <summary>
	///     Largest rules file that is read (1 MiB). A larger file is <see cref="IssueCodes.InvalidJson" /> and is not read,
	///     so a path pointed at the wrong file by mistake cannot stall every options rebuild.
	/// </summary>
	public const long MaxFileSizeBytes = 1024 * 1024;

	private const string ConfigFilePath = $"{PropertyVisibilityOptions.SectionName}:{nameof(PropertyVisibilityOptions.ConfigFile)}";

	// A file that is still being written can be locked for a moment; the change token fires after a debounce, but an
	// editor may hold the file a little longer. Longer locks are retried by ConfigFileReadRetry.
	private static readonly TimeSpan[] ReadRetryDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150)];

	private readonly object _gate = new();
	private readonly IHostEnvironment _hostEnvironment;
	private readonly ConfigurationInfo _info;
	private readonly ConfigFileReadRetry _readRetry;
	private readonly ILogger<ConfigFileOptionsSetup> _logger;
	private readonly PropertyVisibilityOptionsValidator _validator = new();

	// Guarded by _gate.
	private LastGood? _lastGood;
	private string? _lastLoggedFailure;
	private string? _lastLoggedBothSources;
	private string? _lastLoggedLoad;

	/// <summary>
	///     Initializes a new instance with its own retry schedule (nothing retries an unreadable file), for tests.
	/// </summary>
	/// <param name="hostEnvironment">Supplies the content root the file name is resolved against.</param>
	/// <param name="info">Receives the outcome of every load.</param>
	/// <param name="logger">The logger.</param>
	internal ConfigFileOptionsSetup(IHostEnvironment hostEnvironment, ConfigurationInfo info, ILogger<ConfigFileOptionsSetup> logger)
		: this(hostEnvironment, info, new ConfigFileReadRetry(), logger)
	{
	}

	/// <summary>
	///     Initializes a new instance of the <see cref="ConfigFileOptionsSetup" /> class.
	/// </summary>
	/// <param name="hostEnvironment">Supplies the content root the file name is resolved against.</param>
	/// <param name="info">Receives the outcome of every load.</param>
	/// <param name="readRetry">Schedules another read when the file cannot be opened; shared with the change token source.</param>
	/// <param name="logger">The logger.</param>
	internal ConfigFileOptionsSetup(IHostEnvironment hostEnvironment, ConfigurationInfo info, ConfigFileReadRetry readRetry, ILogger<ConfigFileOptionsSetup> logger)
	{
		ArgumentNullException.ThrowIfNull(hostEnvironment);
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(readRetry);
		ArgumentNullException.ThrowIfNull(logger);

		_hostEnvironment = hostEnvironment;
		_info = info;
		_readRetry = readRetry;
		_logger = logger;
	}

	/// <inheritdoc />
	public void Configure(PropertyVisibilityOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		lock (_gate)
		{
			ConfigureLocked(options);
		}
	}

	private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

	private void ConfigureLocked(PropertyVisibilityOptions options)
	{
		var issues = new List<ConfigurationIssue>();
		var configured = options.ConfigFile?.Trim();

		if (string.IsNullOrEmpty(configured))
		{
			ForgetFile();
			_readRetry.ReadSucceeded();
			Publish(ConfigurationSource.Appsettings, filePath: null, options, issues);
			return;
		}

		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(configured, _hostEnvironment.ContentRootPath);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			ForgetFile();
			_readRetry.ReadSucceeded();
			var issue = new ConfigurationIssue(
				IssueCodes.InvalidJson,
				IssueSeverity.Error,
				$"ConfigFile '{configured}' is not a valid path ({ex.Message}); the rules file is ignored and the appsettings rules apply.",
				ConfigFilePath);
			issues.Add(issue);
			LogFailureOnce($"path|{configured}", configured, [issue], fallback: "the appsettings rules apply", ex);
			Publish(ConfigurationSource.Appsettings, filePath: null, options, issues);
			return;
		}

		FileContent content = Read(fullPath);
		if (content.Kind == FileContentKind.Unreadable)
		{
			_readRetry.ReadFailed();
		}
		else
		{
			// Read, gone, or not readable for a reason that time does not fix (Denied): no retry until the next change.
			_readRetry.ReadSucceeded();
		}

		if (content.Kind is FileContentKind.Absent or FileContentKind.NoJsonValue)
		{
			ForgetFile();
			Publish(ConfigurationSource.Appsettings, fullPath, options, issues);
			return;
		}

		ConfigFileParseResult parsed;
		string signature;
		switch (content.Kind)
		{
			case FileContentKind.Unreadable:
				parsed = ConfigFileParseResult.Failure(new ConfigurationIssue(
					IssueCodes.InvalidJson,
					IssueSeverity.Error,
					$"Rules file '{configured}' cannot be read ({content.Error!.Message.TrimEnd('.')}); it is read again automatically until it can be read.",
					ConfigFilePath));
				signature = $"{fullPath}|unreadable|{content.Error.Message}";
				break;

			case FileContentKind.Denied:
				parsed = ConfigFileParseResult.Failure(new ConfigurationIssue(
					IssueCodes.InvalidJson,
					IssueSeverity.Error,
					$"Rules file '{configured}' cannot be read ({content.Error!.Message.TrimEnd('.')}); it is not read again until the file or appsettings changes. Fix the file's permissions or path, then save the file again or restart the site.",
					ConfigFilePath));
				signature = $"{fullPath}|denied|{content.Error.Message}";
				break;

			case FileContentKind.TooLarge:
				var size = content.Length > MaxFileSizeBytes ? $"{content.Length} bytes" : "more than 1 MiB";
				parsed = ConfigFileParseResult.Failure(new ConfigurationIssue(
					IssueCodes.InvalidJson,
					IssueSeverity.Error,
					$"Rules file '{configured}' is {size}, larger than the limit of 1 MiB ({MaxFileSizeBytes} bytes); it is not read. Check that ConfigFile points at the rules file.",
					ConfigFilePath));
				signature = $"{fullPath}|toolarge|{content.Length}";
				break;

			default:
				var contentHash = Sha256(content.Bytes!);
				signature = $"{fullPath}|{contentHash}";
				try
				{
					parsed = ConfigFileParser.Parse(content.Bytes!, configured);
				}
				catch (Exception ex)
				{
					parsed = ConfigFileParseResult.Failure(new ConfigurationIssue(
						IssueCodes.InvalidJson,
						IssueSeverity.Error,
						$"Rules file '{configured}' cannot be loaded: {ex.Message}",
						ConfigFilePath));
				}

				break;
		}

		ConfigFileRules rules;
		ConfigurationSource source;
		if (parsed.Rules is { } good)
		{
			// Only a version that also passes validation becomes the fallback for a later broken edit. A version that fails
			// validation is still applied: the options then fail open until it is fixed (PV008).
			if (PassesValidation(good))
			{
				_lastGood = new LastGood(fullPath, good);
			}

			_lastLoggedFailure = null;
			rules = good;
			source = ConfigurationSource.File;
		}
		else
		{
			issues.AddRange(parsed.Issues);

			// A recovery to any valid content, even the previous one, is logged again.
			_lastLoggedLoad = null;
			if (_lastGood is { } lastGood && PathsEqual(lastGood.FullPath, fullPath))
			{
				rules = lastGood.Rules;
				source = ConfigurationSource.File;
			}
			else
			{
				rules = ConfigFileRules.Empty;
				source = ConfigurationSource.None;
			}

			LogFailureOnce(
				$"{signature}|{string.Join("|", parsed.Issues)}",
				configured,
				parsed.Issues,
				source == ConfigurationSource.File
					? "the last valid version of the file stays in effect"
					: "the file contributes no rules until it is fixed (the appsettings rules stay replaced)",
				content.Error);
		}

		if (options.ContentTypes.Count > 0 || options.Sites.Count > 0)
		{
			issues.Add(new ConfigurationIssue(
				IssueCodes.BothSourcesDefineRules,
				IssueSeverity.Warning,
				$"Both appsettings ({PropertyVisibilityOptions.SectionName}:ContentTypes / Sites) and the rules file '{configured}' define rules; the file wins and the appsettings rules are ignored.",
				PropertyVisibilityOptions.SectionName));
			LogBothSourcesOnce(signature, configured);
		}
		else
		{
			_lastLoggedBothSources = null;
		}

		rules.ApplyTo(options);
		var rulesHash = Publish(source, fullPath, options, issues);

		if (parsed.Rules is not null && !string.Equals(_lastLoggedLoad, signature, StringComparison.Ordinal))
		{
			_lastLoggedLoad = signature;
			_logger.LogInformation("PropertyVisibility: loaded the rules file {ConfigFile} (rules hash {RulesHash}).", configured, rulesHash);
		}
	}

	private bool PassesValidation(ConfigFileRules rules)
	{
		// The validator only looks at ContentTypes and Sites, which the file replaces wholesale.
		var probe = new PropertyVisibilityOptions();
		rules.ApplyTo(probe);
		return IsValid(probe);
	}

	private bool IsValid(PropertyVisibilityOptions options)
		=> !_validator.Collect(options).Any(issue => issue.Severity == IssueSeverity.Error);

	private string Publish(ConfigurationSource source, string? filePath, PropertyVisibilityOptions options, List<ConfigurationIssue> issues)
	{
		var rulesHash = RulesHash.Compute(options);

		// The options monitor validates after this call; options that will fail validation are not a successful load, so
		// the load time and rules hash of the last successful load stay.
		ConfigurationState previous = _info.LoadState;
		_info.Update(IsValid(options)
			? new ConfigurationState(source, filePath, DateTimeOffset.UtcNow, rulesHash, issues.ToArray())
			: new ConfigurationState(source, filePath, previous.LoadedAt, previous.RulesHash, issues.ToArray()));
		return rulesHash;
	}

	private void ForgetFile()
	{
		_lastGood = null;
		_lastLoggedFailure = null;
		_lastLoggedBothSources = null;
		_lastLoggedLoad = null;
	}

	private void LogFailureOnce(string signature, string configured, IReadOnlyList<ConfigurationIssue> issues, string fallback, Exception? exception)
	{
		if (string.Equals(_lastLoggedFailure, signature, StringComparison.Ordinal))
		{
			return;
		}

		_lastLoggedFailure = signature;
		_logger.LogError(
			exception,
			"{IssueCodes}: the PropertyVisibility rules file {ConfigFile} cannot be loaded; {Fallback}. {Issues}",
			string.Join(", ", issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal)),
			configured,
			fallback,
			string.Join(" | ", issues));
	}

	private void LogBothSourcesOnce(string signature, string configured)
	{
		if (string.Equals(_lastLoggedBothSources, signature, StringComparison.Ordinal))
		{
			return;
		}

		_lastLoggedBothSources = signature;
		_logger.LogWarning(
			"{IssueCode}: both appsettings ({Section}:ContentTypes / Sites) and the rules file {ConfigFile} define rules; the file wins and the appsettings rules are ignored.",
			IssueCodes.BothSourcesDefineRules,
			PropertyVisibilityOptions.SectionName,
			configured);
	}

	/// <summary>
	///     Whether a failure to read the rules file can go away by itself: a sharing violation or another I/O error while
	///     a program still writes the file. Denied access and a path that is too long stay until someone changes something.
	/// </summary>
	/// <param name="error">The exception from opening or reading the file.</param>
	/// <returns><c>true</c> when reading again later can succeed.</returns>
	internal static bool IsTransientReadFailure(Exception error)
		=> error is IOException and not PathTooLongException and not FileNotFoundException and not DirectoryNotFoundException;

	private static FileContent Read(string fullPath)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				var file = new FileInfo(fullPath);
				if (!file.Exists)
				{
					return FileContent.Absent;
				}

				// A first check only: the length is 0 for /proc and /dev files, so the read itself is capped as well.
				if (file.Length > MaxFileSizeBytes)
				{
					return new FileContent(FileContentKind.TooLarge, null, null, file.Length);
				}

				if (ReadAtMost(fullPath, MaxFileSizeBytes) is not { } raw)
				{
					return new FileContent(FileContentKind.TooLarge, null, null, 0);
				}

				var bytes = ConfigFileParser.ToUtf8(raw);
				return ConfigFileParser.HasNoJsonValue(bytes)
					? FileContent.NoJsonValue
					: new FileContent(FileContentKind.Content, bytes, null, bytes.Length);
			}
			catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
			{
				return FileContent.Absent;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (!IsTransientReadFailure(ex))
				{
					return new FileContent(FileContentKind.Denied, null, ex, 0);
				}

				if (attempt >= ReadRetryDelays.Length)
				{
					return new FileContent(FileContentKind.Unreadable, null, ex, 0);
				}

				Thread.Sleep(ReadRetryDelays[attempt]);
			}
		}
	}

	// The file's content, or null when it holds more than maxBytes. Shares read access only, as File.ReadAllBytes does,
	// so a file an editor still holds open for writing fails as locked.
	private static byte[]? ReadAtMost(string fullPath, long maxBytes)
	{
		using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
		using var content = new MemoryStream();
		var buffer = new byte[16 * 1024];
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			if (content.Length + read > maxBytes)
			{
				return null;
			}

			content.Write(buffer, 0, read);
		}

		return content.ToArray();
	}

	private static bool PathsEqual(string a, string b)
		=> string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

	private enum FileContentKind
	{
		Absent,
		NoJsonValue,
		Content,

		// Cannot be opened now (locked); read again with a backoff.
		Unreadable,

		// Cannot be opened until someone changes something (access denied, path too long); not read again by itself.
		Denied,
		TooLarge,
	}

	private sealed record FileContent(FileContentKind Kind, byte[]? Bytes, Exception? Error, long Length)
	{
		public static readonly FileContent Absent = new(FileContentKind.Absent, null, null, 0);

		public static readonly FileContent NoJsonValue = new(FileContentKind.NoJsonValue, null, null, 0);
	}

	private sealed record LastGood(string FullPath, ConfigFileRules Rules);
}
