using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Notifications;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     <see cref="ConfigurationAnalysisLogger" />: the analyzer's issues are logged once per configuration change, at
///     startup and on option changes, never twice for the same configuration.
/// </summary>
[TestFixture]
public sealed class ConfigurationAnalyzerLoggingTests
{
	private static readonly ConfigurationAnalysisIssue UnknownProperty = new(
		IssueCodes.UnknownPropertyAlias, IssueSeverity.Warning, "Property 'bannerImg' does not exist.", "corporate", "landingPage", "bannerImage", "Sites:corporate:ContentTypes:landingPage:Properties");

	private static readonly ConfigurationAnalysisIssue Invalid = new(
		IssueCodes.ConfigurationInvalid, IssueSeverity.Error, "The configuration cannot be read.", Path: "PropertyVisibility");

	private static readonly ConfigurationAnalysisIssue Untested = new(
		IssueCodes.UntestedUmbracoVersion, IssueSeverity.Info, "Umbraco 17.7.0 is not in the tested list.", Path: "Umbraco");

	private Mock<IConfigurationAnalyzer> _analyzer = null!;
	private Mock<IOptionsMonitor<PropertyVisibilityOptions>> _monitor = null!;
	private Mock<IDisposable> _subscription = null!;
	private Mock<IRuntimeState> _runtimeState = null!;
	private ListLogger<ConfigurationAnalysisLogger> _logger = null!;
	private Action<PropertyVisibilityOptions, string?>? _onChange;
	private ConfigurationAnalysis _analysis = null!;
	private ConfigurationAnalysisTrigger _trigger = null!;
	private ConfigurationAnalysisLogger _analysisLogger = null!;

	[SetUp]
	public void SetUp()
	{
		_analysis = Analysis("hash-1", UnknownProperty);
		_analyzer = new Mock<IConfigurationAnalyzer>();
		_analyzer.Setup(analyzer => analyzer.Analyze()).Returns(() => _analysis);

		_subscription = new Mock<IDisposable>();
		_monitor = new Mock<IOptionsMonitor<PropertyVisibilityOptions>>();
		_monitor
			.Setup(monitor => monitor.OnChange(It.IsAny<Action<PropertyVisibilityOptions, string?>>()))
			.Callback<Action<PropertyVisibilityOptions, string?>>(listener => _onChange = listener)
			.Returns(_subscription.Object);

		_runtimeState = new Mock<IRuntimeState>();
		_runtimeState.SetupGet(state => state.Level).Returns(RuntimeLevel.Run);

		_logger = new ListLogger<ConfigurationAnalysisLogger>();
		_trigger = new ConfigurationAnalysisTrigger();

		// Synchronous, so every trigger's run has finished when the call returns; the background runner is tested below.
		_analysisLogger = CreateLogger(background: false);
	}

	[TearDown]
	public void TearDown() => _analysisLogger.Dispose();

	[Test]
	public void Startup_logs_every_issue_once_with_its_level()
	{
		_analysis = Analysis("hash-1", Invalid, UnknownProperty, Untested);

		Started();

		Assert.Multiple(() =>
		{
			Assert.That(_logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error, LogLevel.Warning, LogLevel.Information }));
			Assert.That(
				_logger.Entries[1].Message,
				Is.EqualTo("PropertyVisibility PV201 at Sites:corporate:ContentTypes:landingPage:Properties: Property 'bannerImg' does not exist. Did you mean 'bannerImage'?"));
			Assert.That(_logger.Entries[0].Message, Is.EqualTo("PropertyVisibility PV008 at PropertyVisibility: The configuration cannot be read."));
		});
	}

	[Test]
	public void Issues_logged_where_they_are_detected_are_skipped()
	{
		_analysis = Analysis(
			"hash-1",
			new ConfigurationAnalysisIssue(IssueCodes.InvalidJson, IssueSeverity.Error, "Invalid JSON.", Path: "$"),
			new ConfigurationAnalysisIssue(IssueCodes.UnknownKey, IssueSeverity.Error, "Unknown key.", Path: "$.Sites.x.Y"),
			new ConfigurationAnalysisIssue(IssueCodes.BothSourcesDefineRules, IssueSeverity.Warning, "Both sources.", Path: "PropertyVisibility"),
			UnknownProperty);

		Started();

		Assert.That(_logger.Entries.Select(entry => entry.Message), Is.EqualTo(new[] { $"PropertyVisibility PV201 at {UnknownProperty.Path}: {UnknownProperty.MessageWithSuggestion}" }));
	}

	[Test]
	public void A_change_with_the_same_configuration_does_not_log_again()
	{
		Started();
		Changed();
		Changed();

		Assert.Multiple(() =>
		{
			Assert.That(_logger.Entries, Has.Count.EqualTo(1));
			_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Exactly(3));
		});
	}

	[Test]
	public void A_change_with_the_same_configuration_logs_only_new_issues()
	{
		Started();
		_analysis = Analysis("hash-1", UnknownProperty, Untested);

		Changed();

		Assert.That(_logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning, LogLevel.Information }));
	}

	[Test]
	public void A_different_configuration_logs_every_issue_again()
	{
		Started();
		_analysis = Analysis("hash-2", UnknownProperty);

		Changed();

		Assert.That(_logger.Entries, Has.Count.EqualTo(2));
	}

	[Test]
	public void Toggling_the_kill_switch_is_a_different_configuration()
	{
		Started();
		ConfigurationAnalysis analysis = Analysis("hash-1", UnknownProperty);
		_analysis = analysis with { Summary = analysis.Summary with { Enabled = false } };

		Changed();

		Assert.That(_logger.Entries, Has.Count.EqualTo(2));
	}

	[Test]
	public void Nothing_runs_before_Umbraco_is_at_the_Run_level()
	{
		_runtimeState.SetupGet(state => state.Level).Returns(RuntimeLevel.Install);

		Started();
		Changed();

		Assert.Multiple(() =>
		{
			Assert.That(_logger.Entries, Is.Empty);
			_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Never);
		});
	}

	[Test]
	public void A_change_of_a_named_options_instance_is_ignored()
	{
		Assert.That(_onChange, Is.Not.Null);

		_onChange!(new PropertyVisibilityOptions(), "other");

		_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Never);
	}

	[Test]
	public void A_failing_analysis_is_logged_once_per_run_and_never_escapes()
	{
		_analyzer.Setup(analyzer => analyzer.Analyze()).Throws(new InvalidOperationException("database down"));

		Assert.DoesNotThrow(Started);
		Assert.DoesNotThrow(Changed);

		Assert.Multiple(() =>
		{
			Assert.That(_logger.At(LogLevel.Error), Has.Count.EqualTo(2));
			Assert.That(_logger.Entries[0].Exception, Is.InstanceOf<InvalidOperationException>());
		});
	}

	[Test]
	public void Dispose_ends_the_options_subscription_once()
	{
		_analysisLogger.Dispose();
		_analysisLogger.Dispose();

		_subscription.Verify(subscription => subscription.Dispose(), Times.Once);
	}

	[Test]
	public void The_watcher_issue_PV304_is_logged_where_it_is_detected_and_skipped_here()
	{
		_analysis = Analysis(
			"hash-1",
			new ConfigurationAnalysisIssue(IssueCodes.ConfigFileNotWatched, IssueSeverity.Warning, "The rules file is not watched.", Path: "PropertyVisibility:ConfigFile"),
			UnknownProperty);

		Started();

		Assert.That(_logger.Entries.Select(entry => entry.Message), Has.None.Contains(IssueCodes.ConfigFileNotWatched));
	}

	[Test]
	public void A_root_change_after_startup_runs_the_analysis_and_logs_what_is_new()
	{
		Started();
		ConfigurationAnalysisIssue renamedRoot = new(
			IssueCodes.RootNodeNameNotFound, IssueSeverity.Warning, "RootNodeName 'Campaign site' of site 'campaign' matches no root node.", "campaign", Path: "Sites:campaign:RootNodeName");
		_analysis = Analysis("hash-1", UnknownProperty, renamedRoot);

		_trigger.Request();

		Assert.Multiple(() =>
		{
			Assert.That(_logger.At(LogLevel.Warning), Has.Count.EqualTo(2), "the known issue is not logged again");
			Assert.That(_logger.Entries[^1].Message, Does.StartWith("PropertyVisibility PV102 at Sites:campaign:RootNodeName"));
		});
	}

	[Test]
	public void The_trigger_compares_with_the_roots_the_last_run_saw()
	{
		Guid corporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
		Guid campaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");
		ConfigurationAnalysis analysis = Analysis("hash-1", UnknownProperty);
		_analysis = analysis with { Summary = analysis.Summary with { RootNodeKeys = [corporateRoot, campaignRoot] } };
		Started();

		_trigger.RequestIfRootsChanged([campaignRoot, corporateRoot]);
		_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Once);

		_trigger.RequestIfRootsChanged([corporateRoot]);
		_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Exactly(2));
	}

	[Test]
	public void Dispose_ends_the_trigger_subscription()
	{
		_analysisLogger.Dispose();

		_trigger.Request();

		_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Never);
	}

	[Test]
	public async Task In_the_background_triggers_return_at_once_and_merge_into_few_runs()
	{
		_analysisLogger.Dispose();
		using var release = new ManualResetEventSlim(false);
		_analyzer.Setup(analyzer => analyzer.Analyze()).Returns(() =>
		{
			release.Wait(TimeSpan.FromSeconds(10));
			return _analysis;
		});
		using ConfigurationAnalysisLogger background = CreateLogger(background: true, delay: TimeSpan.Zero);

		var stopwatch = Stopwatch.StartNew();
		background.Handle(new UmbracoApplicationStartedNotification(isRestarting: false));
		Changed();
		_trigger.Request();
		Changed();
		stopwatch.Stop();

		release.Set();
		await background.WhenIdle();

		Assert.Multiple(() =>
		{
			Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "a trigger only queues a run; it never waits for the analysis");
			_analyzer.Verify(analyzer => analyzer.Analyze(), Times.AtMost(2), "the triggers during a run merge into one more run");
			Assert.That(_logger.Entries, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task In_the_background_nothing_runs_after_dispose()
	{
		_analysisLogger.Dispose();
		ConfigurationAnalysisLogger background = CreateLogger(background: true, delay: TimeSpan.FromMilliseconds(100));

		Changed();
		background.Dispose();
		await background.WhenIdle();

		_analyzer.Verify(analyzer => analyzer.Analyze(), Times.Never);
	}

	private ConfigurationAnalysisLogger CreateLogger(bool background, TimeSpan? delay = null)
		=> new(_analyzer.Object, _monitor.Object, _runtimeState.Object, _trigger, _logger, delay ?? TimeSpan.Zero, background);

	private static ConfigurationAnalysis Analysis(string rulesHash, params ConfigurationAnalysisIssue[] issues)
		=> new(
			new ConfigurationAnalysisSummary(
				OptionsValid: true,
				Enabled: true,
				ActiveSource: ConfigurationSource.Appsettings,
				FilePath: null,
				LoadedAt: DateTimeOffset.UnixEpoch,
				RulesHash: rulesHash,
				SiteCount: 1,
				RootNodeKeys: [],
				UmbracoVersion: new Version(17, 6, 2),
				TestedUmbracoVersions: [new Version(17, 6, 2)]),
			issues);

	private void Started() => _analysisLogger.Handle(new UmbracoApplicationStartedNotification(isRestarting: false));

	private void Changed() => _onChange!(new PropertyVisibilityOptions(), Microsoft.Extensions.Options.Options.DefaultName);
}
