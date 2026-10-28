namespace Umbraco.Community.PropertyVisibility;

/// <summary>
///     Package-wide constants.
/// </summary>
/// <remarks>
///     Internal: nothing outside the package needs them, and a public <c>const</c> would be compiled into callers (and
///     clash with <c>Umbraco.Cms.Core.Constants</c> in code that imports both namespaces). The unit tests see them through
///     <c>InternalsVisibleTo</c>.
/// </remarks>
internal static class Constants
{
	/// <summary>
	///     Name of the package's own OpenAPI document and the value of its <c>MapToApi</c> attribute.
	///     The document is served at <c>/umbraco/swagger/property-visibility/swagger.json</c> (Umbraco 17) or
	///     <c>/umbraco/openapi/property-visibility.json</c> (Umbraco 18).
	/// </summary>
	public const string ApiName = "property-visibility";

	/// <summary>
	///     Folder under <c>App_Plugins</c> that holds the backoffice client bundle.
	/// </summary>
	public const string PluginFolder = "UmbracoCommunityPropertyVisibility";

#if UMBRACO_18
	/// <summary>
	///     The Umbraco major version this package is built and tested against (package 2.x).
	/// </summary>
	public const int SupportedUmbracoMajor = 18;

	/// <summary>
	///     Umbraco versions the package's acceptance checklist has been run against.
	/// </summary>
	public static readonly IReadOnlyList<Version> TestedUmbracoVersions = [new Version(18, 2, 1), new Version(18, 2, 2)];
#else
	/// <summary>
	///     The Umbraco major version this package is built and tested against (package 1.x).
	/// </summary>
	public const int SupportedUmbracoMajor = 17;

	/// <summary>
	///     Umbraco versions the package's acceptance checklist has been run against.
	/// </summary>
	public static readonly IReadOnlyList<Version> TestedUmbracoVersions = [new Version(17, 6, 2), new Version(17, 7, 0), new Version(17, 7, 1), new Version(17, 7, 2)];
#endif

	/// <summary>
	///     The compatibility notes: which Umbraco versions were verified, and what to do on another one.
	/// </summary>
	public const string CompatibilityDocumentationUrl = "https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/docs/compatibility.md";
}
