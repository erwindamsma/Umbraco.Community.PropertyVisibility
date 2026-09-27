using Umbraco.Community.PropertyVisibility.Api.Models;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Entry point of the server side: options, root, site and rules combined into one response for the backoffice.
/// </summary>
public interface IPropertyVisibilityService
{
	/// <summary>
	///     Computes what to hide for one document (or block) of one content type.
	///     Never throws: every failure yields an empty response so the editor keeps working.
	/// </summary>
	/// <param name="documentKey">Key of the document being edited; for a new document the client-generated key.</param>
	/// <param name="contentTypeKey">Key of the document or element type whose properties are shown.</param>
	/// <param name="parentKey">Key of the parent for a document that does not exist yet, when known.</param>
	/// <returns>The response; never <c>null</c>.</returns>
	HiddenFieldsResponseModel GetHiddenFields(Guid documentKey, Guid contentTypeKey, Guid? parentKey);
}
