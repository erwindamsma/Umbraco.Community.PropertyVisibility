namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     What to hide on one content type. Aliases are matched case-insensitively.
/// </summary>
public sealed class ContentTypeVisibilityOptions
{
	/// <summary>
	///     Property aliases to hide (own or composed).
	/// </summary>
	public List<string> Properties { get; set; } = [];

	/// <summary>
	///     Container aliases to hide: <c>"tabAlias"</c> hides the tab and every group under it,
	///     <c>"tabAlias/groupAlias"</c> hides one group, and a root-level group alias hides that group. The tab alias ends at
	///     the first slash; the group alias after it may contain slashes when the group's name does.
	/// </summary>
	public List<string> Containers { get; set; } = [];
}
