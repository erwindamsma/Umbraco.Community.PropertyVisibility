using Umbraco.Cms.Core.DependencyInjection;
#if UMBRACO_18
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
#else
using Microsoft.Extensions.DependencyInjection;
#endif

namespace Umbraco.Community.PropertyVisibility.Api.OpenApi;

/// <summary>
///     Registers the package's own OpenAPI document, named <see cref="Constants.ApiName" />. Controllers join it through
///     <c>[MapToApi(Constants.ApiName)]</c>, and it carries the backoffice security requirement, so the generated client
///     and the OpenAPI UI authenticate the same way as the Management API.
/// </summary>
/// <remarks>
///     The only server code that differs between the builds: Umbraco 17 registers OpenAPI documents through Swashbuckle's
///     SwaggerGen (<c>/umbraco/swagger/property-visibility/swagger.json</c>), Umbraco 18 through
///     Microsoft.AspNetCore.OpenApi (<c>/umbraco/openapi/property-visibility.json</c>).
/// </remarks>
internal static class PropertyVisibilityOpenApi
{
	public const string Title = "Property Visibility API";

	public const string DocumentVersion = "1.0";

	public const string Description = "Backoffice API of Umbraco.Community.PropertyVisibility: which properties and containers to hide per site.";

	public static IUmbracoBuilder AddPropertyVisibilityOpenApi(this IUmbracoBuilder builder)
	{
#if UMBRACO_18
		return builder.AddBackOfficeOpenApiDocument(
			Constants.ApiName,
			document => document
				.WithTitle(Title)
				.WithBackOfficeAuthentication()
				.ConfigureOpenApiOptions(options => options.AddDocumentTransformer((openApiDocument, _, _) =>
				{
					openApiDocument.Info.Version = DocumentVersion;
					openApiDocument.Info.Description = Description;
					return Task.CompletedTask;
				})));
#else
		builder.Services.ConfigureOptions<PropertyVisibilitySwaggerGenOptionsSetup>();
		return builder;
#endif
	}
}
