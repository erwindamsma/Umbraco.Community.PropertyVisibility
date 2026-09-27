using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Api.Controllers;

/// <summary>
///     Returns the property types and containers to hide for one document (or block) of one content type.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = Constants.ApiName)]
public sealed class HiddenFieldsController : PropertyVisibilityControllerBase
{
	private readonly IPropertyVisibilityService _service;

	/// <summary>
	///     Initializes a new instance of the <see cref="HiddenFieldsController" /> class.
	/// </summary>
	/// <param name="service">The visibility service.</param>
	public HiddenFieldsController(IPropertyVisibilityService service) => _service = service;

	/// <summary>
	///     <c>GET /umbraco/property-visibility/v1/hiddenfields?documentKey=&amp;contentTypeKey=&amp;parentKey=</c>.
	/// </summary>
	/// <param name="documentKey">Key of the document being edited; for a new document the client-generated key.</param>
	/// <param name="contentTypeKey">Key of the document or element type whose properties are shown.</param>
	/// <param name="parentKey">Key of the parent for a document that does not exist yet, when known.</param>
	/// <returns>
	///     The keys to hide: <c>200</c> for every well-formed request, with empty lists on any failure; <c>400</c> (from
	///     <c>[ApiController]</c> model validation) when a key is not a GUID.
	/// </returns>
	[HttpGet]
	[ProducesResponseType<HiddenFieldsResponseModel>(StatusCodes.Status200OK)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
	public IActionResult Get([FromQuery] Guid documentKey, [FromQuery] Guid contentTypeKey, [FromQuery] Guid? parentKey = null)
	{
		Response.Headers.CacheControl = "no-store";
		return Ok(_service.GetHiddenFields(documentKey, contentTypeKey, parentKey));
	}
}
