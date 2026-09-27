namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Rules for one site, identified by its root node.
/// </summary>
/// <remarks>
///     A site is matched by <see cref="RootNodeKey" /> first, then by <see cref="RootNodeName" />,
///     and the single <see cref="IsDefault" /> site catches every root that nothing else matched.
/// </remarks>
public sealed class SiteVisibilityOptions
{
	/// <summary>
	///     Key of the site's root node. Survives renames; the recommended identity.
	/// </summary>
	public Guid? RootNodeKey { get; set; }

	/// <summary>
	///     Invariant name of the site's root node, compared case-insensitively and trimmed.
	///     Used when no site matched by key. On a variant site this is the default-culture name as of the last save.
	/// </summary>
	public string? RootNodeName { get; set; }

	/// <summary>
	///     Marks the site whose rules apply to every root node no other site matched. At most one site may be the default.
	/// </summary>
	public bool IsDefault { get; set; }

	/// <summary>
	///     Rules for this site, keyed by content type alias (document or element type). A rule keyed by a composition
	///     also applies to every type composed of it, where it hides only what the composition contributes.
	/// </summary>
	public Dictionary<string, ContentTypeVisibilityOptions> ContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
