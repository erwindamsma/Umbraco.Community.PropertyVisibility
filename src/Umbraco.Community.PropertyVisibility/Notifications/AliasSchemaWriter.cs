using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Notifications;

/// <summary>
///     In the Development environment, writes <see cref="FileName" /> next to the rules file: the rules file schema with
///     this site's aliases and root nodes (<see cref="AliasSchemaBuilder" />). A rules file whose <c>$schema</c> points at
///     it gets IntelliSense on the real aliases and root node keys, and typos are underlined while they are typed.
/// </summary>
/// <remarks>
///     <para>
///         Writes only while the rules file exists, so a site that keeps its rules in appsettings gets no extra file. Runs
///         at startup, when a document or element type changes, when a root node is created, renamed, moved, trashed or
///         deleted (<see cref="ConfigurationAnalysisTrigger" />), and when the options change, which covers a rules file
///         that was just created. Requests are merged and run in the background, like
///         <see cref="ConfigurationAnalysisLogger" />.
///     </para>
///     <para>
///         The file is only written when its content changes, through a temporary file in the same folder, so an unchanged
///         site leaves it untouched (a file watcher such as <c>dotnet watch</c> sees nothing). Outside Development nothing
///         is subscribed and nothing is written. A failure is logged once per distinct message and never reaches the
///         operation that triggered the run.
///     </para>
/// </remarks>
public sealed class AliasSchemaWriter :
	INotificationHandler<UmbracoApplicationStartedNotification>,
	INotificationHandler<ContentTypeChangedNotification>,
	IDisposable
{
	/// <summary>
	///     Name of the written file, next to the rules file. <c>PropertyVisibility.config-schema*.json</c> in a site's
	///     .gitignore covers it and the schema the build copies.
	/// </summary>
	public const string FileName = "PropertyVisibility.config-schema.local.json";

	/// <summary>
	///     How long requests are merged before a run.
	/// </summary>
	public static readonly TimeSpan RunDelay = TimeSpan.FromSeconds(1);

	private readonly object _scheduleGate = new();
	private readonly object _runGate = new();
	private readonly IOptionsMonitor<PropertyVisibilityOptions> _options;
	private readonly IConfigurationInfo _configurationInfo;
	private readonly IContentTypeService _contentTypeService;
	private readonly IDocumentNavigationQueryService _navigation;
	private readonly IRootNodeResolver _rootNodeResolver;
	private readonly IRuntimeState _runtimeState;
	private readonly ILogger<AliasSchemaWriter> _logger;
	private readonly bool _enabled;
	private readonly TimeSpan _delay;
	private readonly bool _background;
	private IDisposable? _changeSubscription;
	private IDisposable? _triggerSubscription;

	// Guarded by _runGate.
	private string? _lastFailure;
	private bool _announced;

	// Guarded by _scheduleGate.
	private bool _dirty;
	private Task? _worker;
	private bool _disposed;

	/// <summary>
	///     Initializes a new instance of the <see cref="AliasSchemaWriter" /> class.
	/// </summary>
	/// <param name="options">The options monitor; a change asks for a run.</param>
	/// <param name="configurationInfo">The path of the rules file of the latest load.</param>
	/// <param name="contentTypeService">The document and element types.</param>
	/// <param name="navigation">The document tree, for the root node keys.</param>
	/// <param name="rootNodeResolver">Root node names.</param>
	/// <param name="runtimeState">Runs only once Umbraco is installed and running.</param>
	/// <param name="hostEnvironment">Writes only in the Development environment.</param>
	/// <param name="trigger">Asks for a run when the root nodes change.</param>
	/// <param name="logger">The logger.</param>
	public AliasSchemaWriter(
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IConfigurationInfo configurationInfo,
		IContentTypeService contentTypeService,
		IDocumentNavigationQueryService navigation,
		IRootNodeResolver rootNodeResolver,
		IRuntimeState runtimeState,
		IHostEnvironment hostEnvironment,
		ConfigurationAnalysisTrigger trigger,
		ILogger<AliasSchemaWriter> logger)
		: this(options, configurationInfo, contentTypeService, navigation, rootNodeResolver, runtimeState, hostEnvironment, trigger, logger, RunDelay, background: true)
	{
	}

	/// <summary>
	///     Initializes a new instance of the <see cref="AliasSchemaWriter" /> class with a merge delay; tests run it
	///     synchronously.
	/// </summary>
	internal AliasSchemaWriter(
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IConfigurationInfo configurationInfo,
		IContentTypeService contentTypeService,
		IDocumentNavigationQueryService navigation,
		IRootNodeResolver rootNodeResolver,
		IRuntimeState runtimeState,
		IHostEnvironment hostEnvironment,
		ConfigurationAnalysisTrigger trigger,
		ILogger<AliasSchemaWriter> logger,
		TimeSpan delay,
		bool background)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(hostEnvironment);
		ArgumentNullException.ThrowIfNull(trigger);

		_options = options;
		_configurationInfo = configurationInfo;
		_contentTypeService = contentTypeService;
		_navigation = navigation;
		_rootNodeResolver = rootNodeResolver;
		_runtimeState = runtimeState;
		_logger = logger;
		_delay = delay;
		_background = background;
		_enabled = hostEnvironment.IsDevelopment();

		if (!_enabled)
		{
			return;
		}

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

	/// <inheritdoc />
	public void Handle(ContentTypeChangedNotification notification) => RequestRun();

	/// <summary>
	///     Queues a run; returns at once. Does nothing outside Development.
	/// </summary>
	public void RequestRun()
	{
		if (!_enabled)
		{
			return;
		}

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
	///     Writes the schema now when the rules file exists and the content differs from the file's. Never throws.
	/// </summary>
	public void Run()
	{
		if (!_enabled || _runtimeState.Level != RuntimeLevel.Run)
		{
			return;
		}

		lock (_runGate)
		{
			string? schemaPath = null;
			try
			{
				schemaPath = SchemaPath();
				if (schemaPath is null)
				{
					return;
				}

				var json = AliasSchemaBuilder.Build(_contentTypeService.GetAll(), Roots());
				if (File.Exists(schemaPath) && string.Equals(File.ReadAllText(schemaPath), json, StringComparison.Ordinal))
				{
					_lastFailure = null;
					return;
				}

				WriteAtomically(schemaPath, json);
				_lastFailure = null;
				if (!_announced)
				{
					_announced = true;
					_logger.LogInformation(
						"PropertyVisibility wrote {SchemaPath} with this site's aliases and root nodes. For IntelliSense on them, start the rules file with \"$schema\": \"./{SchemaFile}\".",
						schemaPath,
						FileName);
				}
				else
				{
					_logger.LogDebug("PropertyVisibility updated {SchemaPath}.", schemaPath);
				}
			}
			catch (Exception ex)
			{
				var failure = $"{ex.GetType().FullName}|{ex.Message}";
				if (!string.Equals(failure, _lastFailure, StringComparison.Ordinal))
				{
					_lastFailure = failure;
					_logger.LogWarning(ex, "PropertyVisibility could not write the alias schema {SchemaPath}; the rules file still works without it.", schemaPath ?? FileName);
				}
			}
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
	///     Completes when the queued runs are done; for tests.
	/// </summary>
	internal Task WhenIdle()
	{
		lock (_scheduleGate)
		{
			return _worker ?? Task.CompletedTask;
		}
	}

	private static void WriteAtomically(string path, string content)
	{
		var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(temporary, content);
			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	// Next to the rules file of the latest load, while that file exists; null otherwise (file source off, or no file).
	private string? SchemaPath()
	{
		try
		{
			// Builds the options when needed, which refreshes the configuration info. A configuration that cannot be read
			// (PV008) keeps the path of the last load.
			_ = _options.CurrentValue;
		}
		catch (Exception ex) when (ex is OptionsValidationException or InvalidOperationException)
		{
		}

		var rulesFile = _configurationInfo.FilePath;
		return rulesFile is not null && File.Exists(rulesFile) && Path.GetDirectoryName(rulesFile) is { } folder
			? Path.Combine(folder, FileName)
			: null;
	}

	private IEnumerable<AliasSchemaRoot> Roots()
		=> _navigation.TryGetRootKeys(out IEnumerable<Guid> rootKeys)
			? rootKeys.Select(key => new AliasSchemaRoot(key, _rootNodeResolver.GetRootName(key))).ToArray()
			: [];

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
