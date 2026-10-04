using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Notifications;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;
using File = System.IO.File;

namespace Umbraco.Community.PropertyVisibility.Tests.Notifications;

/// <summary>
///     <see cref="AliasSchemaWriter" /> against a temporary content root, run synchronously.
/// </summary>
[TestFixture]
public sealed class AliasSchemaWriterTests
{
	private const string RulesFile = "PropertyVisibility.config.json";

	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");

	private TestHostEnvironment _host = null!;
	private Mock<IOptionsMonitor<PropertyVisibilityOptions>> _options = null!;
	private Mock<IConfigurationInfo> _info = null!;
	private Mock<IRuntimeState> _runtimeState = null!;
	private ConfigurationAnalysisTrigger _trigger = null!;
	private ListLogger<AliasSchemaWriter> _logger = null!;
	private List<IContentType> _contentTypes = null!;
	private Dictionary<Guid, string> _roots = null!;
	private string? _filePath;
	private Exception? _optionsFailure;
	private readonly List<AliasSchemaWriter> _writers = [];

	private string SchemaPath => Path.Combine(_host.ContentRootPath, AliasSchemaWriter.FileName);

	[SetUp]
	public void SetUp()
	{
		_host = new TestHostEnvironment();
		_filePath = Path.Combine(_host.ContentRootPath, RulesFile);
		_optionsFailure = null;
		_contentTypes = [Document("landingPage").NamedTab("seoTab", "Seo tab").NamedGroup("seoTab/meta", "Meta", "metaTitle").Build()];
		_roots = new Dictionary<Guid, string> { [CorporateRoot] = "Corporate site" };

		_options = new Mock<IOptionsMonitor<PropertyVisibilityOptions>>();
		_options.SetupGet(monitor => monitor.CurrentValue).Returns(() => _optionsFailure is null ? new PropertyVisibilityOptions() : throw _optionsFailure);
		_info = new Mock<IConfigurationInfo>();
		_info.SetupGet(info => info.FilePath).Returns(() => _filePath);
		_runtimeState = new Mock<IRuntimeState>();
		_runtimeState.SetupGet(state => state.Level).Returns(RuntimeLevel.Run);
		_trigger = new ConfigurationAnalysisTrigger();
		_logger = new ListLogger<AliasSchemaWriter>();
	}

	[TearDown]
	public void TearDown()
	{
		foreach (AliasSchemaWriter writer in _writers)
		{
			writer.Dispose();
		}

		_writers.Clear();
		_host.Dispose();
	}

	[Test]
	public void Without_a_rules_file_nothing_is_written()
	{
		CreateWriter().Run();

		Assert.That(File.Exists(SchemaPath), Is.False);
	}

	[Test]
	public void With_a_rules_file_the_schema_is_written_next_to_it_and_announced_once()
	{
		_host.WriteFile(RulesFile, "{}");
		AliasSchemaWriter writer = CreateWriter();

		writer.Run();
		_contentTypes.Add(Element("promoBanner").NamedGroup("main", "Main", "overlayColour").Build());
		writer.Run();

		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(SchemaPath), Is.EqualTo(ExpectedSchema()));
			Assert.That(_logger.At(LogLevel.Information), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Information)[0].Message, Does.Contain($"\"$schema\": \"./{AliasSchemaWriter.FileName}\""));
			Assert.That(_logger.At(LogLevel.Debug), Has.Count.EqualTo(1), "the second write is logged at Debug");
			Assert.That(Directory.GetFiles(_host.ContentRootPath, "*.tmp"), Is.Empty);
		});
	}

	[Test]
	public void An_unchanged_site_leaves_the_file_untouched()
	{
		_host.WriteFile(RulesFile, "{}");
		AliasSchemaWriter writer = CreateWriter();
		writer.Run();
		var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(SchemaPath, past);

		writer.Run();

		Assert.That(File.GetLastWriteTimeUtc(SchemaPath), Is.EqualTo(past));
	}

	[Test]
	public void A_changed_root_node_rewrites_the_schema_through_the_trigger()
	{
		_host.WriteFile(RulesFile, "{}");
		CreateWriter().Run();

		_roots[CorporateRoot] = "Corporate";
		_trigger.Request();

		Assert.That(File.ReadAllText(SchemaPath), Does.Contain("\"Corporate\"").And.Not.Contain("\"Corporate site\""));
	}

	[Test]
	public void The_writer_handles_startup_and_content_type_changes()
		=> Assert.That(
			typeof(AliasSchemaWriter).GetInterfaces(),
			Is.SupersetOf(new[]
			{
				typeof(INotificationHandler<UmbracoApplicationStartedNotification>),
				typeof(INotificationHandler<ContentTypeChangedNotification>),
			}));

	[TestCase("Production")]
	[TestCase("Staging")]
	public void Outside_Development_nothing_is_written(string environment)
	{
		_host.EnvironmentName = environment;
		_host.WriteFile(RulesFile, "{}");
		AliasSchemaWriter writer = CreateWriter();

		writer.Run();
		writer.RequestRun();
		_trigger.Request();

		Assert.That(File.Exists(SchemaPath), Is.False);
	}

	[Test]
	public void With_the_file_source_off_nothing_is_written()
	{
		_host.WriteFile(RulesFile, "{}");
		_filePath = null;

		CreateWriter().Run();

		Assert.That(File.Exists(SchemaPath), Is.False);
	}

	[Test]
	public void Before_Umbraco_runs_nothing_is_written()
	{
		_host.WriteFile(RulesFile, "{}");
		_runtimeState.SetupGet(state => state.Level).Returns(RuntimeLevel.Install);

		CreateWriter().Run();

		Assert.That(File.Exists(SchemaPath), Is.False);
	}

	[Test]
	public void Options_that_cannot_be_read_keep_the_path_of_the_last_load()
	{
		_host.WriteFile(RulesFile, "{ \"Sites\": { \"s\": { \"IsDefault\": \"yes\" } } }");
		_optionsFailure = new InvalidOperationException("Failed to convert configuration value");

		CreateWriter().Run();

		Assert.That(File.Exists(SchemaPath), Is.True);
	}

	[Test]
	public void A_failed_write_is_logged_once_and_never_thrown()
	{
		_host.WriteFile(RulesFile, "{}");
		Directory.CreateDirectory(SchemaPath);
		AliasSchemaWriter writer = CreateWriter();

		Assert.DoesNotThrow(writer.Run);
		Assert.DoesNotThrow(writer.Run);

		Assert.Multiple(() =>
		{
			Assert.That(_logger.At(LogLevel.Warning), Has.Count.EqualTo(1));
			Assert.That(_logger.At(LogLevel.Warning)[0].Message, Does.Contain(SchemaPath));
			Assert.That(Directory.GetFiles(_host.ContentRootPath, "*.tmp"), Is.Empty);
		});
	}

	private AliasSchemaWriter CreateWriter()
	{
		var contentTypeService = new Mock<IContentTypeService>();
		contentTypeService.Setup(service => service.GetAll()).Returns(() => _contentTypes);
		var navigation = new Mock<IDocumentNavigationQueryService>();
		navigation
			.Setup(service => service.TryGetRootKeys(out It.Ref<IEnumerable<Guid>>.IsAny))
			.Returns(new TryGetRootKeys((out IEnumerable<Guid> keys) =>
			{
				keys = _roots.Keys.ToArray();
				return true;
			}));
		var rootNodeResolver = new Mock<IRootNodeResolver>();
		rootNodeResolver.Setup(resolver => resolver.GetRootName(It.IsAny<Guid>())).Returns((Guid key) => _roots.GetValueOrDefault(key));

		var writer = new AliasSchemaWriter(
			_options.Object,
			_info.Object,
			contentTypeService.Object,
			navigation.Object,
			rootNodeResolver.Object,
			_runtimeState.Object,
			_host,
			_trigger,
			_logger,
			TimeSpan.Zero,
			background: false);
		_writers.Add(writer);
		return writer;
	}

	private string ExpectedSchema()
		=> AliasSchemaBuilder.Build(_contentTypes, _roots.Select(root => new AliasSchemaRoot(root.Key, root.Value)));

	private delegate bool TryGetRootKeys(out IEnumerable<Guid> keys);
}
