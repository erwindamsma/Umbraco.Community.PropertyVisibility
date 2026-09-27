using Microsoft.Extensions.Logging;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Community.PropertyVisibility.Tests.Configuration;
using Umbraco.Community.PropertyVisibility.Tests.TestSupport;
using static Umbraco.Community.PropertyVisibility.Tests.TestSupport.TestContentType;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

/// <summary>
///     The service against a real options monitor bound from a JSON file that is rewritten and reloaded, so binding
///     failures, validation failures and the options monitor's change notifications behave as in a running site.
/// </summary>
[TestFixture]
public sealed class PropertyVisibilityServiceConfigurationTests
{
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid CorporatePage = Guid.Parse("11111111-1111-4111-8111-111111111111");

	private static readonly string Valid = JsonConfiguration.Appsettings(PropertyVisibilityOptionsBindingTests.Sample);
	private static readonly string EnabledIsNotABoolean = Valid.Replace("\"Enabled\": true", "\"Enabled\": \"nope\"", StringComparison.Ordinal);
	private static readonly string RootNodeKeyIsMistyped = Valid.Replace(
		"\"RootNodeKey\": \"5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b\"",
		"\"RootNodeKey\": \"5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7\"",
		StringComparison.Ordinal);

	private JsonConfiguration _configuration = null!;
	private ListLogger<PropertyVisibilityService> _logger = null!;
	private IContentType _landingPage = null!;
	private PropertyVisibilityService _service = null!;

	[SetUp]
	public void SetUp()
	{
		Assert.That(EnabledIsNotABoolean, Is.Not.EqualTo(Valid), "precondition");
		Assert.That(RootNodeKeyIsMistyped, Is.Not.EqualTo(Valid), "precondition");

		_configuration = new JsonConfiguration(Valid);

		_landingPage = Document("landingPage")
			.Tab("contentTab")
			.Group("contentTab/main", "title", "bannerImage", "relatedLinks")
			.Build();

		var contentTypeService = new Mock<IContentTypeService>();
		contentTypeService.Setup(service => service.Get(_landingPage.Key)).Returns(_landingPage);

		var rootNodeResolver = new Mock<IRootNodeResolver>();
		rootNodeResolver
			.Setup(resolver => resolver.Resolve(CorporatePage, It.IsAny<Guid?>()))
			.Returns(new RootNodeResolution(CorporateRoot, RootResolutionSource.Document));
		rootNodeResolver.Setup(resolver => resolver.GetRootName(CorporateRoot)).Returns("Corporate site");

		_logger = new ListLogger<PropertyVisibilityService>();
		_service = new PropertyVisibilityService(
			_configuration.Monitor,
			contentTypeService.Object,
			rootNodeResolver.Object,
			new SiteMatcher(rootNodeResolver.Object),
			new HiddenFieldsResolver(),
			_logger);
	}

	[TearDown]
	public void TearDown()
	{
		_service.Dispose();
		_configuration.Dispose();
	}

	[Test]
	public void Valid_configuration_matches_the_site_by_key()
	{
		HiddenFieldsResponseModel response = Request();

		Assert.Multiple(() =>
		{
			Assert.That(response.MatchedSite?.Label, Is.EqualTo("corporate"));
			Assert.That(response.MatchedSite?.Reason, Is.EqualTo(SiteMatchReason.Key));
			Assert.That(response.PropertyTypeKeys, Is.EquivalentTo(PropertyKeys(_landingPage, "bannerImage", "relatedLinks")));
			Assert.That(ErrorLogs(), Is.Empty);
		});
	}

	[Test]
	public void A_value_that_cannot_be_converted_fails_open_with_one_error_log()
	{
		using var configuration = new JsonConfiguration(EnabledIsNotABoolean);
		var service = new PropertyVisibilityService(
			configuration.Monitor,
			Mock.Of<IContentTypeService>(),
			Mock.Of<IRootNodeResolver>(),
			Mock.Of<ISiteMatcher>(),
			new HiddenFieldsResolver(),
			_logger);

		HiddenFieldsResponseModel first = service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		HiddenFieldsResponseModel second = service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);
		service.Dispose();

		Assert.Multiple(() =>
		{
			foreach (HiddenFieldsResponseModel response in new[] { first, second })
			{
				Assert.That(response.Disabled, Is.False);
				Assert.That(response.PropertyTypeKeys, Is.Empty);
				Assert.That(response.ContainerKeys, Is.Empty);
				Assert.That(response.MatchedSite, Is.Null);
				Assert.That(response.Warnings, Has.Count.EqualTo(1).And.One.StartsWith(IssueCodes.ConfigurationInvalid));
			}

			Assert.That(ErrorLogs(), Has.Count.EqualTo(1));
			Assert.That(ErrorLogs()[0].Exception, Is.InstanceOf<InvalidOperationException>());
			Assert.That(ErrorLogs()[0].Message, Does.Contain("PropertyVisibility:Enabled"));
		});
	}

	[Test]
	public void A_mistyped_root_node_key_fails_open_instead_of_matching_the_default_site()
	{
		_configuration.Change(RootNodeKeyIsMistyped);

		HiddenFieldsResponseModel response = Request();

		Assert.Multiple(() =>
		{
			Assert.That(response.MatchedSite, Is.Null, "without ErrorOnUnknownConfiguration the corporate site vanished and everythingElse matched by default");
			Assert.That(response.PropertyTypeKeys, Is.Empty);
			Assert.That(response.Warnings, Has.One.StartsWith(IssueCodes.ConfigurationInvalid));
			Assert.That(ErrorLogs(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Each_distinct_failure_is_logged_once_across_reloads_and_a_valid_configuration_re_arms_the_log()
	{
		Assert.That(Request().MatchedSite?.Label, Is.EqualTo("corporate"));

		_configuration.Change(EnabledIsNotABoolean);
		Request();
		Request();
		Assert.That(ErrorLogs(), Has.Count.EqualTo(1), "first failure, logged once");

		// Invalid replaced by a different invalid configuration: the options monitor calls no change listener here.
		_configuration.Change(RootNodeKeyIsMistyped);
		Request();
		Request();
		Assert.That(ErrorLogs(), Has.Count.EqualTo(2), "a different failure is logged once");

		_configuration.Change(Valid);
		Assert.That(Request().MatchedSite?.Label, Is.EqualTo("corporate"), "valid again");
		Assert.That(ErrorLogs(), Has.Count.EqualTo(2));

		_configuration.Change(EnabledIsNotABoolean);
		Request();
		Assert.That(ErrorLogs(), Has.Count.EqualTo(3), "the first failure again, after a valid configuration");
	}

	private HiddenFieldsResponseModel Request() => _service.GetHiddenFields(CorporatePage, _landingPage.Key, parentKey: null);

	private IReadOnlyList<LogEntry> ErrorLogs() => _logger.At(LogLevel.Error);
}
