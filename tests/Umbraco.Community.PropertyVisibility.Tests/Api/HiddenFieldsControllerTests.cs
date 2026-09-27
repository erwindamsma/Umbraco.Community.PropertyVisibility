using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Community.PropertyVisibility.Api.Controllers;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Tests.Api;

[TestFixture]
public sealed class HiddenFieldsControllerTests
{
	[Test]
	public void Get_returns_200_with_the_service_response_and_disables_caching()
	{
		var documentKey = Guid.NewGuid();
		var contentTypeKey = Guid.NewGuid();
		var parentKey = Guid.NewGuid();
		var expected = new HiddenFieldsResponseModel
		{
			PropertyTypeKeys = [Guid.NewGuid()],
			ContainerKeys = [Guid.NewGuid()],
			RootResolution = RootResolutionSource.Parent,
			MatchedSite = new MatchedSiteModel { Label = "corporate", Reason = SiteMatchReason.Key },
		};
		var service = new Mock<IPropertyVisibilityService>(MockBehavior.Strict);
		service.Setup(s => s.GetHiddenFields(documentKey, contentTypeKey, parentKey)).Returns(expected);
		HiddenFieldsController controller = CreateController(service.Object);

		IActionResult result = controller.Get(documentKey, contentTypeKey, parentKey);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.TypeOf<OkObjectResult>());
			var ok = (OkObjectResult)result;
			Assert.That(ok.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
			Assert.That(ok.Value, Is.SameAs(expected));
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
		});
		service.Verify(s => s.GetHiddenFields(documentKey, contentTypeKey, parentKey), Times.Once);
	}

	[Test]
	public void Get_without_parent_key_passes_null_to_the_service()
	{
		var documentKey = Guid.NewGuid();
		var contentTypeKey = Guid.NewGuid();
		HiddenFieldsResponseModel expected = HiddenFieldsResponseModel.Empty(RootResolutionSource.Document, []);
		var service = new Mock<IPropertyVisibilityService>(MockBehavior.Strict);
		service.Setup(s => s.GetHiddenFields(documentKey, contentTypeKey, null)).Returns(expected);
		HiddenFieldsController controller = CreateController(service.Object);

		IActionResult result = controller.Get(documentKey, contentTypeKey);

		Assert.Multiple(() =>
		{
			Assert.That(((OkObjectResult)result).Value, Is.SameAs(expected));
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
		});
	}

	[Test]
	public void Disabled_response_is_still_a_200()
	{
		HiddenFieldsResponseModel disabled = HiddenFieldsResponseModel.DisabledResponse();
		var service = new Mock<IPropertyVisibilityService>();
		service.Setup(s => s.GetHiddenFields(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>())).Returns(disabled);
		HiddenFieldsController controller = CreateController(service.Object);

		IActionResult result = controller.Get(Guid.NewGuid(), Guid.NewGuid());

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.TypeOf<OkObjectResult>());
			var body = (HiddenFieldsResponseModel)((OkObjectResult)result).Value!;
			Assert.That(body.Disabled, Is.True);
			Assert.That(body.PropertyTypeKeys, Is.Empty);
			Assert.That(body.ContainerKeys, Is.Empty);
		});
	}

	[Test]
	public void The_API_requires_an_approved_backoffice_user_with_access_to_the_Content_section()
	{
		// BackOfficeAccess is what every Umbraco Management API controller requires; both policies must be met.
		var policies = typeof(HiddenFieldsController)
			.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
			.Cast<AuthorizeAttribute>()
			.Select(attribute => attribute.Policy)
			.ToList();

		Assert.That(policies, Is.EquivalentTo(new[] { AuthorizationPolicies.BackOfficeAccess, AuthorizationPolicies.SectionAccessContent }));
	}

	private static HiddenFieldsController CreateController(IPropertyVisibilityService service)
		=> new(service)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};
}
