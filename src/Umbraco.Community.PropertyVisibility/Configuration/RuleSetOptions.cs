namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Rules that several sites share: a site uses them by naming the set in <see cref="SiteVisibilityOptions.Include" />.
/// </summary>
/// <remarks>
///     A site's rules are its own <see cref="SiteVisibilityOptions.ContentTypes" /> united with those of every rule set it
///     includes, so a rule set can only add to what a site hides. A rule set cannot include another rule set.
/// </remarks>
public sealed class RuleSetOptions
{
	/// <summary>
	///     Rules of this set, keyed by content type alias (document or element type). A rule keyed by a composition also
	///     applies to every type composed of it, where it hides only what the composition contributes.
	/// </summary>
	public Dictionary<string, ContentTypeVisibilityOptions> ContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
