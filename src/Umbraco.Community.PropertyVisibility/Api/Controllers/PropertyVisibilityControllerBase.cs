using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Umbraco.Community.PropertyVisibility.Api.Controllers;

/// <summary>
///     Base for the package's backoffice API controllers: routed under <c>/umbraco/property-visibility/v{version}/</c>,
///     restricted to approved backoffice users with access to the Content section, mapped to the package's own OpenAPI
///     document.
/// </summary>
/// <remarks>
///     <c>BackOfficeAccess</c> is the policy every Umbraco Management API controller carries (an authenticated, approved
///     backoffice user); <c>SectionAccessContent</c> adds the Content section. There is no per-node permission check: the
///     response is a list of property and container keys that any Content-section user can already see in the document
///     type editor.
/// </remarks>
[ApiController]
[BackOfficeRoute("property-visibility/v{version:apiVersion}/[controller]")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
[MapToApi(Constants.ApiName)]
public abstract class PropertyVisibilityControllerBase : ControllerBase
{
}
