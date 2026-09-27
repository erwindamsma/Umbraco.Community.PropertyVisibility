using Umbraco.Cms.Api.Management.OpenApi;

namespace Umbraco.Community.PropertyVisibility.Api.OpenApi;

/// <summary>
///     Adds the backoffice security requirement (and the 401 response) to every operation of the package's OpenAPI
///     document, so the generated client and Swagger UI authenticate the same way as the Management API.
/// </summary>
/// <remarks>
///     Together with <see cref="PropertyVisibilitySwaggerGenOptionsSetup" /> this is the only server code that depends
///     on the OpenAPI registration stack, which Umbraco 18 changes.
/// </remarks>
public sealed class PropertyVisibilitySecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
	/// <inheritdoc />
	protected override string ApiName => Constants.ApiName;
}
