using System.Text.Json.Serialization;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Api.Models;

/// <summary>
///     The site whose rules were applied to a request.
/// </summary>
/// <remarks>
///     Deliberately carries no root node key or name: any user with Content section access can call the API for any
///     document key (no start node or document permission check), so the label and reason are all it learns about the
///     site. Users with access to the Settings section find the site's root in the health check.
/// </remarks>
public sealed class MatchedSiteModel
{
	/// <summary>
	///     The site's label, its key under <c>PropertyVisibility:Sites</c>.
	/// </summary>
	public required string Label { get; init; }

	/// <summary>
	///     Which tier matched: <c>Key</c>, <c>Name</c> or <c>Default</c>.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public SiteMatchReason Reason { get; init; }
}
