using System.Text.Json.Serialization;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Api.Models;

/// <summary>
///     Response of <c>GET /umbraco/property-visibility/v1/hiddenfields</c>.
/// </summary>
public sealed class HiddenFieldsResponseModel
{
	/// <summary>
	///     Keys of the property types to hide, including every property of a hidden container.
	/// </summary>
	public required IReadOnlyList<Guid> PropertyTypeKeys { get; init; }

	/// <summary>
	///     Keys of the containers (tabs and groups) to remove from the workspace structure.
	/// </summary>
	public required IReadOnlyList<Guid> ContainerKeys { get; init; }

	/// <summary>
	///     <c>true</c> when the package is switched off (<c>PropertyVisibility:Enabled</c> is <c>false</c>); both lists are then empty.
	/// </summary>
	public bool Disabled { get; init; }

	/// <summary>
	///     How the root node was resolved.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public RootResolutionSource RootResolution { get; init; }

	/// <summary>
	///     The site whose rules were applied, or <c>null</c> when only global rules applied.
	/// </summary>
	public MatchedSiteModel? MatchedSite { get; init; }

	/// <summary>
	///     Diagnostics for this request, each prefixed with its issue code: unmatched aliases on this content type,
	///     name drift, no matching site. Never contains a root node key or name; the server log and the health check
	///     carry those details.
	/// </summary>
	public IReadOnlyList<string> Warnings { get; init; } = [];

	/// <summary>
	///     Creates a response that hides nothing.
	/// </summary>
	/// <param name="rootResolution">How the root was resolved, when known.</param>
	/// <param name="warnings">Diagnostics to pass to the client.</param>
	/// <returns>The response.</returns>
	public static HiddenFieldsResponseModel Empty(RootResolutionSource rootResolution, IReadOnlyList<string> warnings)
		=> new()
		{
			PropertyTypeKeys = [],
			ContainerKeys = [],
			RootResolution = rootResolution,
			Warnings = warnings,
		};

	/// <summary>
	///     Creates the response for a disabled package.
	/// </summary>
	/// <returns>The response.</returns>
	public static HiddenFieldsResponseModel DisabledResponse()
		=> new()
		{
			PropertyTypeKeys = [],
			ContainerKeys = [],
			Disabled = true,
			RootResolution = RootResolutionSource.None,
			Warnings = [$"{Configuration.IssueCodes.Disabled}: PropertyVisibility is disabled."],
		};
}
