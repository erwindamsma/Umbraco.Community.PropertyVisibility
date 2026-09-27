using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Why a site was (or was not) matched.
/// </summary>
public enum SiteMatchReason
{
	/// <summary>No site matched.</summary>
	None,

	/// <summary>Matched on <see cref="SiteVisibilityOptions.RootNodeKey" />.</summary>
	Key,

	/// <summary>Matched on <see cref="SiteVisibilityOptions.RootNodeName" />.</summary>
	Name,

	/// <summary>Fell through to the site marked <see cref="SiteVisibilityOptions.IsDefault" />.</summary>
	Default,
}

/// <summary>
///     Result of matching a root node to a configured site.
/// </summary>
/// <param name="Label">The site's label (its key in <see cref="PropertyVisibilityOptions.Sites" />), or <c>null</c> when nothing matched.</param>
/// <param name="Site">The matched site's options, or <c>null</c> when nothing matched.</param>
/// <param name="Reason">Which tier produced the match.</param>
/// <param name="Issues">Diagnostics raised while matching, such as a name drift (<see cref="IssueCodes.RootNodeNameDrift" />).</param>
public sealed record SiteMatch(
	string? Label,
	SiteVisibilityOptions? Site,
	SiteMatchReason Reason,
	IReadOnlyList<ConfigurationIssue> Issues)
{
	/// <summary>
	///     Shared instance for "no site matched" without diagnostics.
	/// </summary>
	public static readonly SiteMatch None = new(null, null, SiteMatchReason.None, []);
}

/// <summary>
///     Matches a resolved root node to one of the configured sites.
/// </summary>
public interface ISiteMatcher
{
	/// <summary>
	///     Matches in three tiers: root key, then root name (case-insensitive, trimmed; a site whose <c>RootNodeKey</c> is a
	///     root node takes no part, its name is only compared for drift), then the default site. A resolution without a root
	///     key skips the first two tiers; a recycle bin resolution matches nothing.
	/// </summary>
	/// <param name="options">The current options.</param>
	/// <param name="resolution">The root node resolution of the request.</param>
	/// <returns>The match; <see cref="SiteMatch.Reason" /> is <see cref="SiteMatchReason.None" /> when nothing matched.</returns>
	SiteMatch Match(PropertyVisibilityOptions options, RootNodeResolution resolution);
}
