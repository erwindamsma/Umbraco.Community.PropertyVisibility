using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Notifications;

/// <summary>
///     Logs the issues of <see cref="IConfigurationAnalyzer" /> once per configuration change, never per request: a first
///     run when Umbraco has started (<see cref="UmbracoApplicationStartedNotification" />), then a run on every change of
///     <see cref="PropertyVisibilityOptions" /> and whenever the root nodes change (<see cref="ConfigurationAnalysisTrigger" />).
/// </summary>
/// <remarks>
///     <para>
///         Registered as a singleton that is also the startup notification handler, so its subscriptions live as long as
///         the application. Runs only at <see cref="RuntimeLevel.Run" /> (not during install or upgrade).
///     </para>
///     <para>
///         Runs in the background: a trigger only queues a run, <see cref="RunDelay" /> later, on a thread-pool thread.
///         Triggers that arrive while a run is queued or running merge into one more run. The triggers come from threads
///         others wait on (the appsettings reload runs every options listener in turn, the rules file's timer, the host's
///         started callback, content saves), and an analysis reads every content type and the root names.
///     </para>
///     <para>
///         Deduplication: an issue (its code, path, message and suggestion) is logged once per configuration, identified by
///         the rules hash, the kill switch and whether the options are valid. The options monitor raises a change for every
///         appsettings reload, including edits to unrelated sections and the watcher's duplicate events; such a change
///         with the same configuration only logs issues that were not there before (a deleted content type, a renamed root).
///         A different configuration logs every issue again.
///     </para>
///     <para>
///         Issues that are logged where they are detected are skipped: <see cref="IssueCodes.InvalidJson" />,
///         <see cref="IssueCodes.UnknownKey" /> and <see cref="IssueCodes.BothSourcesDefineRules" /> by the rules file loader,
///         <see cref="IssueCodes.ConfigFileNotWatched" /> by the rules file watcher. <see cref="IssueCodes.ConfigurationInvalid" />
///         and the validator codes are logged here only by the startup run and by root change runs: an options change that
///         fails to build raises no change event, and the hidden-fields service logs such a failure on the next request.
///     </para>
///     <para>
///         Severities map to <see cref="LogLevel.Error" />, <see cref="LogLevel.Warning" /> and <see cref="LogLevel.Information" />.
///         Every entry uses the template <c>PropertyVisibility {IssueCode} at {ConfigPath}: {IssueMessage}</c>. An analysis
///         that throws is logged as one error and never escapes.
///     </para>
/// </remarks>
public sealed class ConfigurationAnalysisLogger : INotificationHandler<UmbracoApplicationStartedNotification>, IDisposable
{
	/// <summary>
	///     Time between a trigger and the run it queues; triggers within it merge into that run.
	/// </summary>
	public static readonly TimeSpan RunDelay = TimeSpan.FromSeconds(1);

	private static readonly HashSet<string> LoggedWhereDetected = new(StringComparer.Ordinal)
	{
		IssueCodes.InvalidJson,
		IssueCodes.UnknownKey,
		IssueCodes.BothSourcesDefineRules,
		IssueCodes.ConfigFileNotWatched,
	};

	private readonly object _gate = new();
	private readonly object _scheduleGate = new();
	private readonly IConfigurationAnalyzer _analyzer;
	private readonly IRuntimeState _runtimeState;
	private readonly ConfigurationAnalysisTrigger _trigger;
	private readonly ILogger<ConfigurationAnalysisLogger> _logger;
	private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
	private readonly TimeSpan _delay;
	private readonly bool _background;
	private IDisposable? _changeSubscription;
	private IDisposable? _triggerSubscription;

	// Guarded by _gate.
	private string? _configuration;

	// Guarded by _scheduleGate.
	private bool _dirty;
	private Task? _worker;
	private bool _disposed;

	/// <summary>
	///     Initializes a new instance of the <see cref="ConfigurationAnalysisLogger" /> class and subscribes to option
	///     changes and root node changes.
	/// </summary>
	/// <param name="analyzer">Produces the issues.</param>
	/// <param name="options">The options monitor whose changes trigger a run.</param>
	/// <param name="runtimeState">Runs are skipped unless the runtime level is <see cref="RuntimeLevel.Run" />.</param>
	/// <param name="trigger">Root node changes, reported by the content notification handlers.</param>
	/// <param name="logger">The logger.</param>
	public ConfigurationAnalysisLogger(
		IConfigurationAnalyzer analyzer,
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IRuntimeState runtimeState,
		ConfigurationAnalysisTrigger trigger,
		ILogger<ConfigurationAnalysisLogger> logger)
		: this(analyzer, options, runtimeState, trigger, logger, RunDelay, background: true)
	{
	}

	/// <summary>
	///     Initializes a new instance with a custom delay, or running every trigger synchronously, for tests.
	/// </summary>
	internal ConfigurationAnalysisLogger(
		IConfigurationAnalyzer analyzer,
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IRuntimeState runtimeState,
		ConfigurationAnalysisTrigger trigger,
		ILogger<ConfigurationAnalysisLogger> logger,
		TimeSpan delay,
		bool background)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(trigger);

		_analyzer = analyzer;
		_runtimeState = runtimeState;
		_trigger = trigger;
		_logger = logger;
		_delay = delay;
		_background = background;
		_changeSubscription = options.OnChange((_, name) =>
		{
			if (name is null || name == Options.DefaultName)
			{
				RequestRun();
			}
		});
		_triggerSubscription = trigger.Subscribe(RequestRun);
	}

	/// <inheritdoc />
	public void Handle(UmbracoApplicationStartedNotification notification) => RequestRun();

	/// <summary>
	///     Queues a run in the background (<see cref="RunDelay" /> from now), or joins the run already queued. Returns at once.
	/// </summary>
	public void RequestRun()
	{
		if (!_background)
		{
			Run();
			return;
		}

		lock (_scheduleGate)
		{
			if (_disposed)
			{
				return;
			}

			_dirty = true;
			_worker ??= Task.Run(DrainAsync);
		}
	}

	/// <summary>
	///     Analyzes the configuration now, on the calling thread, and logs every issue not yet logged for the current
	///     configuration.
	/// </summary>
	public void Run()
	{
		if (_runtimeState.Level != RuntimeLevel.Run)
		{
			return;
		}

		try
		{
			lock (_gate)
			{
				ConfigurationAnalysis analysis = _analyzer.Analyze();
				ConfigurationAnalysisSummary summary = analysis.Summary;
				_trigger.Analyzed(summary.RootNodeKeys);

				var configuration = $"{summary.OptionsValid}|{summary.Enabled}|{summary.RulesHash}";
				if (!string.Equals(configuration, _configuration, StringComparison.Ordinal))
				{
					_configuration = configuration;
					_logged.Clear();
				}

				foreach (ConfigurationAnalysisIssue issue in analysis.Issues)
				{
					if (LoggedWhereDetected.Contains(issue.Code) || !_logged.Add(issue.ToString()))
					{
						continue;
					}

					_logger.Log(
						ToLogLevel(issue.Severity),
						"PropertyVisibility {IssueCode} at {ConfigPath}: {IssueMessage}",
						issue.Code,
						issue.Path ?? PropertyVisibilityOptions.SectionName,
						issue.MessageWithSuggestion);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "PropertyVisibility: the configuration analysis failed; the health check shows the current state.");
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		lock (_scheduleGate)
		{
			_disposed = true;
		}

		Interlocked.Exchange(ref _changeSubscription, null)?.Dispose();
		Interlocked.Exchange(ref _triggerSubscription, null)?.Dispose();
	}

	/// <summary>
	///     The queued or running background work; a completed task when there is none. For tests.
	/// </summary>
	internal Task WhenIdle()
	{
		lock (_scheduleGate)
		{
			return _worker ?? Task.CompletedTask;
		}
	}

	private static LogLevel ToLogLevel(IssueSeverity severity) => severity switch
	{
		IssueSeverity.Error => LogLevel.Error,
		IssueSeverity.Warning => LogLevel.Warning,
		_ => LogLevel.Information,
	};

	private async Task DrainAsync()
	{
		while (true)
		{
			await Task.Delay(_delay).ConfigureAwait(false);

			lock (_scheduleGate)
			{
				if (_disposed || !_dirty)
				{
					_worker = null;
					return;
				}

				_dirty = false;
			}

			Run();
		}
	}
}
