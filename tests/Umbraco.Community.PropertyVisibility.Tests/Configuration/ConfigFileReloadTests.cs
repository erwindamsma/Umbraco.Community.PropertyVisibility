using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Api.Controllers;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Composing;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Configuration;

/// <summary>
///     The package's registrations in a real service provider with a real <see cref="IOptionsMonitor{TOptions}" />, a
///     real <c>PhysicalFileProvider</c> on a temporary content root and the real 250 ms debounce: file edits reach
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> without a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ConfigFileReloadTests
{
	private const string FileName = PropertyVisibilityOptions.DefaultConfigFile;
	private const int ReloadTimeoutMilliseconds = 5000;
	private const int PollingMilliseconds = 50;

	private TestHostEnvironment _environment = null!;
	private ServiceProvider? _services;

	[SetUp]
	public void SetUp() => _environment = new TestHostEnvironment();

	[TearDown]
	public void TearDown()
	{
		_services?.Dispose();
		_services = null;
		_environment.Dispose();
	}

	[Test]
	public void A_burst_of_writes_fires_one_change_and_the_monitor_serves_the_last_content()
	{
		_environment.WriteFile(FileName, Rules("first"));
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(new ConfigurationBuilder().Build());
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));

		var changes = 0;
		using IDisposable? subscription = monitor.OnChange(_ => Interlocked.Increment(ref changes));

		for (var index = 0; index < 5; index++)
		{
			Write(FileName, Rules($"burst{index}"));
			Thread.Sleep(20);
		}

		Write(FileName, Rules("final"));

		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "final" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));

		// Well past the debounce: nothing else may arrive for the same burst.
		Thread.Sleep(4 * (int)ConfigFileChangeTokenSource.Debounce.TotalMilliseconds);
		Assert.That(Volatile.Read(ref changes), Is.EqualTo(1), "one change for the whole burst");
	}

	[Test]
	public void Creating_and_deleting_the_file_switches_between_the_sources()
	{
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["PropertyVisibility:ContentTypes:fromAppsettings:Properties:0"] = "bannerImage",
			})
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();

		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromAppsettings" }));
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
		});

		Write(FileName, Rules("fromFile"));
		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromFile" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
		Assert.Multiple(() =>
		{
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
			Assert.That(info.Issues.Single().Code, Is.EqualTo(IssueCodes.BothSourcesDefineRules));
		});

		_environment.DeleteFile(FileName);
		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromAppsettings" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
		Assert.Multiple(() =>
		{
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(info.Issues, Is.Empty);
		});
	}

	[Test]
	public void An_invalid_edit_is_reported_and_the_last_good_rules_keep_serving()
	{
		_environment.WriteFile(FileName, Rules("good"));
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(new ConfigurationBuilder().Build());
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "good" }));
		var loadedAt = info.LoadedAt;

		Write(FileName, """{ "ContentTypes": { "broken": { "Propertes": [] } } }""");

		Assert.That(() => info.LoadedAt != loadedAt, Is.True.After(ReloadTimeoutMilliseconds, PollingMilliseconds), "the edit rebuilt the options");
		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "good" }));
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.File));
			Assert.That(info.Issues.Single().Suggestion, Is.EqualTo("Properties"));
		});
	}

	[Test]
	public void Changing_ConfigFile_in_appsettings_moves_the_watch_to_the_new_file()
	{
		var appsettings = Path.Combine(_environment.ContentRootPath, "appsettings.json");
		File.WriteAllText(appsettings, """{ "PropertyVisibility": { "ConfigFile": "a.json" } }""");
		_environment.WriteFile("a.json", Rules("fromA"));
		_environment.WriteFile("b.json", Rules("fromB"));
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(_environment.ContentRootPath)
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		var source = _services!.GetServices<IOptionsChangeTokenSource<PropertyVisibilityOptions>>().OfType<ConfigFileChangeTokenSource>().Single();
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromA" }));

		File.WriteAllText(appsettings, """{ "PropertyVisibility": { "ConfigFile": "b.json" } }""");
		configuration.Reload();

		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromB" }), "the appsettings change rebuilds the options");
			Assert.That(source.WatchedPath, Is.EqualTo(Path.Combine(_environment.ContentRootPath, "b.json")));
		});

		Write("b.json", Rules("fromBEdited"));
		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromBEdited" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
	}

	[Test]
	public void A_file_outside_the_content_root_is_watched_too()
	{
		var elsewhere = TemporaryFolder.Create();
		try
		{
			var path = Path.Combine(elsewhere, "rules.json");
			File.WriteAllText(path, Rules("outside"));
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = path })
				.Build();
			IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "outside" }));

			WriteWithRetry(path, Rules("outsideEdited"));

			Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "outsideEdited" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
		}
		finally
		{
			_services?.Dispose();
			_services = null;
			TemporaryFolder.Delete(elsewhere);
		}
	}

	[Test]
	public void An_empty_ConfigFile_watches_nothing()
	{
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = string.Empty })
			.Build();
		Build(configuration);

		var source = _services!.GetServices<IOptionsChangeTokenSource<PropertyVisibilityOptions>>().OfType<ConfigFileChangeTokenSource>().Single();

		Assert.That(source.WatchedPath, Is.Null);
	}

	[Test]
	public void The_registrations_share_one_configuration_info()
	{
		Build(new ConfigurationBuilder().Build());
		ServiceProvider services = _services!;

		Assert.Multiple(() =>
		{
			Assert.That(services.GetRequiredService<IConfigurationInfo>(), Is.SameAs(services.GetRequiredService<ConfigurationInfo>()));
			Assert.That(
				services.GetServices<IConfigureOptions<PropertyVisibilityOptions>>().Last(),
				Is.InstanceOf<ConfigFileOptionsSetup>(),
				"the file setup runs after the appsettings binding");
			Assert.That(services.GetServices<IOptionsChangeTokenSource<PropertyVisibilityOptions>>().OfType<ConfigFileChangeTokenSource>().Single().Name, Is.EqualTo(Options.DefaultName));
		});
	}

	[Test]
	public void A_watch_that_cannot_start_leaves_the_options_and_the_API_working_and_reports_PV304()
	{
		// As for a content root the watcher cannot read, or an exhausted inotify instance limit.
		_environment.WatchFailure = new FileNotFoundException("Error reading the content root directory.");
		_environment.WriteFile(FileName, Rules("fromFile"));
		var watchLogger = new ListLogger<ConfigFileChangeTokenSource>();

		IOptionsMonitor<PropertyVisibilityOptions> monitor = null!;
		Assert.DoesNotThrow(() => monitor = Build(new ConfigurationBuilder().Build(), watchLogger), "the options monitor must resolve");
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		ConfigFileChangeTokenSource source = TokenSource();

		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "fromFile" }), "the file is still read");
			Assert.That(source.WatchedPath, Is.Null);
			ConfigurationIssue issue = info.Current.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.ConfigFileNotWatched));
			Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Warning));
			Assert.That(issue.Path, Is.EqualTo("PropertyVisibility:ConfigFile"));
			Assert.That(issue.Message, Does.Contain("is not watched").And.Contain("Error reading the content root directory").And.Contain("DOTNET_USE_POLLING_FILE_WATCHER=1"));
			Assert.That(watchLogger.At(LogLevel.Warning), Has.Count.EqualTo(1));
			Assert.That(watchLogger.At(LogLevel.Warning)[0].Message, Does.StartWith(IssueCodes.ConfigFileNotWatched));
		});

		// The hidden-fields endpoint answers 200 with the service built on this monitor.
		var contentTypeService = new Mock<IContentTypeService>();
		var rootNodeResolver = new Mock<IRootNodeResolver>();
		rootNodeResolver.Setup(resolver => resolver.Resolve(It.IsAny<Guid>(), It.IsAny<Guid?>())).Returns(RootNodeResolution.None);
		using var service = new PropertyVisibilityService(
			monitor,
			contentTypeService.Object,
			rootNodeResolver.Object,
			new SiteMatcher(rootNodeResolver.Object),
			new HiddenFieldsResolver(),
			new ListLogger<PropertyVisibilityService>());
		var controller = new HiddenFieldsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

		IActionResult result = controller.Get(Guid.NewGuid(), Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.TypeOf<OkObjectResult>());
			var body = (HiddenFieldsResponseModel)((OkObjectResult)result).Value!;
			Assert.That(body.PropertyTypeKeys, Is.Empty);
			Assert.That(body.ContainerKeys, Is.Empty);
		});
	}

	[Test]
	public void A_watch_that_fails_when_re_armed_still_applies_that_edit_and_the_next_configuration_reload_restarts_it()
	{
		var path = _environment.WriteFile(FileName, Rules("first"));
		IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
		var watchLogger = new ListLogger<ConfigFileChangeTokenSource>();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration, watchLogger);
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		ConfigFileChangeTokenSource source = TokenSource();
		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));
			Assert.That(source.WatchedPath, Is.EqualTo(path));
		});

		_environment.WatchFailure = new IOException("The configured user limit on the number of inotify instances has been reached.");
		Write(FileName, Rules("second"));

		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "second" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds), "the edit that fired is applied");
		Assert.That(() => source.WatchedPath, Is.Null.After(ReloadTimeoutMilliseconds, PollingMilliseconds), "the dead watch is forgotten");
		Assert.Multiple(() =>
		{
			Assert.That(info.Current.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.ConfigFileNotWatched }));
			Assert.That(watchLogger.At(LogLevel.Warning), Has.Count.EqualTo(1));
		});

		_environment.WatchFailure = null;
		configuration.Reload();

		Assert.Multiple(() =>
		{
			Assert.That(source.WatchedPath, Is.EqualTo(path));
			Assert.That(info.Current.Issues, Is.Empty, "PV304 clears once the file is watched again");
			Assert.That(watchLogger.At(LogLevel.Information).Select(entry => entry.Message), Has.One.Contains("is watched again"));
		});

		Write(FileName, Rules("third"));
		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "third" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
	}

	[Test]
	public void A_rules_file_in_a_folder_that_does_not_exist_is_PV304()
	{
		var missing = Path.Combine(Path.GetTempPath(), "pv-tests-missing-" + Guid.NewGuid().ToString("N"), "rules.json");
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = missing })
			.Build();

		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();

		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ContentTypes, Is.Empty);
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			ConfigurationIssue issue = info.Current.Issues.Single();
			Assert.That(issue.Code, Is.EqualTo(IssueCodes.ConfigFileNotWatched));
			Assert.That(issue.Message, Does.Contain("does not exist"));
		});
	}

	[Test]
	public void Turning_the_file_source_off_after_a_failed_watch_clears_PV304()
	{
		var appsettings = Path.Combine(_environment.ContentRootPath, "appsettings.json");
		var missing = Path.Combine(Path.GetTempPath(), "pv-tests-missing-" + Guid.NewGuid().ToString("N"), "rules.json");
		File.WriteAllText(appsettings, $$"""{ "PropertyVisibility": { "ConfigFile": {{System.Text.Json.JsonSerializer.Serialize(missing)}} } }""");
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(_environment.ContentRootPath)
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		Assert.That(monitor.CurrentValue.ConfigFile, Is.EqualTo(missing));
		Assert.That(info.Current.Issues.Select(issue => issue.Code), Is.EqualTo(new[] { IssueCodes.ConfigFileNotWatched }), "precondition: the folder does not exist");

		File.WriteAllText(appsettings, """{ "PropertyVisibility": { "ConfigFile": "" } }""");
		configuration.Reload();

		Assert.Multiple(() =>
		{
			Assert.That(monitor.CurrentValue.ConfigFile, Is.Empty);
			Assert.That(TokenSource().WatchedPath, Is.Null);
			Assert.That(info.Current.Issues, Is.Empty, "nothing is watched, and nothing needs to be");
		});
	}

	[TestCase(".pv-rules.json", TestName = "A dot-prefixed rules file in the content root is reloaded on an edit")]
	[TestCase(".config/rules.json", TestName = "A rules file in a dot-prefixed folder of the content root is reloaded on an edit")]
	public void A_rules_file_the_content_root_provider_ignores_is_still_reloaded(string relativePath)
	{
		// The content root's PhysicalFileProvider uses ExclusionFilters.Sensitive, which drops the events of dot-prefixed,
		// hidden and system files.
		Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_environment.ContentRootPath, relativePath))!);
		_environment.WriteFile(relativePath, Rules("first"));
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = relativePath })
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));

		Write(relativePath, Rules("second"));

		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "second" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
	}

	[Test]
	public void A_hidden_rules_file_in_the_content_root_is_reloaded_on_an_edit()
	{
		if (!OperatingSystem.IsWindows())
		{
			Assert.Ignore("The Hidden attribute can be set on Windows only; elsewhere hidden means dot-prefixed.");
		}

		var path = _environment.WriteFile("hidden-rules.json", Rules("first"));
		File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = "hidden-rules.json" })
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));

		// File.WriteAllText cannot open a hidden file with FileMode.Create on Windows; overwrite it in place.
		WriteInPlace(path, Rules("second"));

		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "second" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
	}

	[Test]
	public void A_rules_file_that_becomes_hidden_is_watched_again_after_the_next_configuration_reload()
	{
		if (!OperatingSystem.IsWindows())
		{
			Assert.Ignore("The Hidden attribute can be set on Windows only; elsewhere hidden means dot-prefixed.");
		}

		var path = _environment.WriteFile(FileName, Rules("first"));
		IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));

		// Hidden after the watch started (a sync tool or an attrib +h): the content root's watcher drops its events from
		// now on. A configuration reload notices and moves the file to a watcher of its own.
		File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
		configuration.Reload();

		WriteInPlace(path, Rules("second"));

		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "second" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
	}

	[Test]
	public void A_dot_prefixed_rules_file_outside_the_content_root_is_reloaded_on_an_edit()
	{
		var elsewhere = TemporaryFolder.Create();
		try
		{
			var path = Path.Combine(elsewhere, ".rules.json");
			File.WriteAllText(path, Rules("outside"));
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?> { ["PropertyVisibility:ConfigFile"] = path })
				.Build();
			IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "outside" }));

			WriteWithRetry(path, Rules("outsideEdited"));

			Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "outsideEdited" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
		}
		finally
		{
			_services?.Dispose();
			_services = null;
			TemporaryFolder.Delete(elsewhere);
		}
	}

	[Test]
	public void A_file_still_locked_when_it_is_read_is_read_again_after_the_lock_is_released()
	{
		var path = _environment.WriteFile(FileName, Rules("first"));
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(new ConfigurationBuilder().Build());
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));

		using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			locked.SetLength(0);
			locked.Write(Encoding.UTF8.GetBytes(Rules("second")));
			locked.Flush(flushToDisk: true);

			Assert.That(
				() => info.Issues.Any(issue => issue.Code == IssueCodes.InvalidJson && issue.Message.Contains("cannot be read", StringComparison.Ordinal)),
				Is.True.After(ReloadTimeoutMilliseconds, PollingMilliseconds),
				"the edit was read while the writer still held the file");
			Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }), "the last valid version meanwhile");

			// Past the first retry (1 second after the failed read): the file is still locked then.
			Thread.Sleep(1500);
		}

		// Releasing the lock raises no file event; the scheduled retry reads the file.
		Assert.That(() => monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "second" }).After(10_000, 100));
		Assert.That(info.Issues, Is.Empty, "PV001 clears");
	}

	[Test]
	public void Options_that_fail_validation_in_the_real_pipeline_are_not_a_successful_load()
	{
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["PropertyVisibility:Sites:a:IsDefault"] = "true",
				["PropertyVisibility:Sites:b:IsDefault"] = "true",
			})
			.Build();
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(configuration);
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();

		Assert.Throws<OptionsValidationException>(() => _ = monitor.CurrentValue);
		Assert.Multiple(() =>
		{
			Assert.That(info.ActiveSource, Is.EqualTo(ConfigurationSource.Appsettings));
			Assert.That(info.LoadedAt, Is.Null, "no build ever succeeded");
			Assert.That(info.RulesHash, Is.Null);
		});
	}

	[Test]
	public void An_edit_that_fails_validation_fails_open_and_a_later_broken_edit_restores_the_last_valid_version()
	{
		_environment.WriteFile(FileName, Rules("first"));
		IOptionsMonitor<PropertyVisibilityOptions> monitor = Build(new ConfigurationBuilder().Build());
		IConfigurationInfo info = _services!.GetRequiredService<IConfigurationInfo>();
		Assert.That(monitor.CurrentValue.ContentTypes.Keys, Is.EquivalentTo(new[] { "first" }));
		DateTimeOffset? loadedAt = info.LoadedAt;
		var rulesHash = info.RulesHash;

		// Parses, but a site without identity (PV003): the options fail validation and nothing is hidden.
		Write(FileName, """{ "Sites": { "noIdentity": { "ContentTypes": { "article": { "Properties": ["title"] } } } } }""");
		Assert.That(() => ContentTypeKeysOrNull(monitor), Is.Null.After(ReloadTimeoutMilliseconds, PollingMilliseconds), "CurrentValue throws");
		Assert.Multiple(() =>
		{
			Assert.That(info.LoadedAt, Is.EqualTo(loadedAt), "the rejected build is not the last successful load");
			Assert.That(info.RulesHash, Is.EqualTo(rulesHash));
		});

		// A syntax error next: the last valid version (not the rejected one) is what stays in effect.
		Write(FileName, """{ "ContentTypes": """);
		Assert.That(() => ContentTypeKeysOrNull(monitor), Is.EquivalentTo(new[] { "first" }).After(ReloadTimeoutMilliseconds, PollingMilliseconds));
		Assert.Multiple(() =>
		{
			Assert.That(info.Issues.Single().Code, Is.EqualTo(IssueCodes.InvalidJson));
			Assert.That(info.RulesHash, Is.EqualTo(rulesHash));
		});
	}

	private static string[]? ContentTypeKeysOrNull(IOptionsMonitor<PropertyVisibilityOptions> monitor)
	{
		try
		{
			return monitor.CurrentValue.ContentTypes.Keys.ToArray();
		}
		catch (OptionsValidationException)
		{
			return null;
		}
	}

	private static string Rules(string contentTypeAlias) => $$"""{ "ContentTypes": { "{{contentTypeAlias}}": { "Properties": ["bannerImage"] } } }""";

	private ConfigFileChangeTokenSource TokenSource()
		=> _services!.GetServices<IOptionsChangeTokenSource<PropertyVisibilityOptions>>().OfType<ConfigFileChangeTokenSource>().Single();

	// A rebuild may be reading the file at the moment of a write; File.ReadAllBytes shares read access only.
	private static void WriteWithRetry(string path, string content)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				File.WriteAllText(path, content);
				return;
			}
			catch (IOException) when (attempt < 20)
			{
				Thread.Sleep(25);
			}
		}
	}

	private static void WriteInPlace(string path, string content)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
				stream.SetLength(0);
				stream.Write(Encoding.UTF8.GetBytes(content));
				return;
			}
			catch (IOException) when (attempt < 20)
			{
				Thread.Sleep(25);
			}
		}
	}

	private IOptionsMonitor<PropertyVisibilityOptions> Build(IConfiguration configuration, ILogger<ConfigFileChangeTokenSource>? watchLogger = null)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IHostEnvironment>(_environment);
		services.AddLogging();
		if (watchLogger is not null)
		{
			services.AddSingleton(watchLogger);
		}

		UmbracoBuilderExtensions.AddPropertyVisibilityOptions(services, configuration);
		UmbracoBuilderExtensions.AddPropertyVisibilityConfigFile(services, configuration);
		_services = services.BuildServiceProvider();
		return _services.GetRequiredService<IOptionsMonitor<PropertyVisibilityOptions>>();
	}

	private void Write(string relativePath, string content) => WriteWithRetry(Path.Combine(_environment.ContentRootPath, relativePath), content);
}
