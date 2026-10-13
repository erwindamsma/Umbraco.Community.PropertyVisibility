#if UMBRACO_17
using Umbraco.Cms.Api.Management.OpenApi;

namespace Umbraco.Community.PropertyVisibility.Api.OpenApi;

/// <summary>
///     Adds the backoffice security requirement (and the 401 response) to every operation of the package's OpenAPI
///     document, so the generated client and Swagger UI authenticate the same way as the Management API.
/// </summary>
/// <remarks>
///     Umbraco 17 only (package 1.x): Umbraco 18 replaced Swashbuckle's SwaggerGen, and the 2.x build registers the
///     document through <see cref="PropertyVisibilityOpenApi" />.
/// </remarks>
public sealed class PropertyVisibilitySecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
	/// <inheritdoc />
	protected override string ApiName => Constants.ApiName;
}
#endif
