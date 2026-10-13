#if UMBRACO_17
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Umbraco.Community.PropertyVisibility.Api.OpenApi;

/// <summary>
///     Registers the package's own OpenAPI document (<c>/umbraco/swagger/property-visibility/swagger.json</c>) and its
///     security filter. Controllers join the document through <c>[MapToApi(Constants.ApiName)]</c>; Umbraco's
///     <c>DocumentInclusionSelector</c> does the rest. The default operation-id handler is kept on purpose: replacing it
///     is a global, last-package-wins side effect.
/// </summary>
/// <remarks>
///     Umbraco 17 only (package 1.x): Umbraco 18 replaced Swashbuckle's SwaggerGen, and the 2.x build registers the
///     document through <see cref="PropertyVisibilityOpenApi" />.
/// </remarks>
public sealed class PropertyVisibilitySwaggerGenOptionsSetup : IConfigureOptions<SwaggerGenOptions>
{
	/// <inheritdoc />
	public void Configure(SwaggerGenOptions options)
	{
		options.SwaggerDoc(
			Constants.ApiName,
			new OpenApiInfo
			{
				Title = PropertyVisibilityOpenApi.Title,
				Version = PropertyVisibilityOpenApi.DocumentVersion,
				Description = PropertyVisibilityOpenApi.Description,
			});

		options.OperationFilter<PropertyVisibilitySecurityFilter>();
	}
}
#endif
