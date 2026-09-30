namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Root options for the package, bound from the <c>PropertyVisibility</c> appsettings section.
/// </summary>
/// <remarks>
///     The model is free of Umbraco types so the JSON schema generator can reflect over it.
///     Consumers read it through <c>IOptionsMonitor&lt;PropertyVisibilityOptions&gt;.CurrentValue</c>.
/// </remarks>
public sealed class PropertyVisibilityOptions
{
	/// <summary>
	///     Name of the appsettings section the options are bound from.
	/// </summary>
	public const string SectionName = "PropertyVisibility";

	/// <summary>
	///     Kill switch. When <c>false</c> nothing is hidden and the API reports <c>disabled</c>. Default <c>true</c>.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	///     Default value of <see cref="ConfigFile" />.
	/// </summary>
	public const string DefaultConfigFile = "PropertyVisibility.config.json";

	/// <summary>
	///     Optional rules file relative to the content root. An empty value disables the file source.
	///     When the file exists and parses it replaces <see cref="ContentTypes" />, <see cref="RuleSets" /> and
	///     <see cref="Sites" /> wholesale.
	/// </summary>
	/// <remarks>
	///     A missing file, or one holding no JSON value (empty or only comments), is not an error: the appsettings rules
	///     apply. The file is watched and reloaded without a restart. <see cref="Enabled" /> and <see cref="ConfigFile" />
	///     can only be set in appsettings.
	/// </remarks>
	public string? ConfigFile { get; set; } = DefaultConfigFile;

	/// <summary>
	///     Also hide a group whose properties are all hidden, and a tab whose groups and own properties are all hidden.
	///     Default <c>true</c>. The rules file overrides it only when the file sets it.
	/// </summary>
	public bool HideEmptiedContainers { get; set; } = true;

	/// <summary>
	///     Rules that apply on every site, keyed by content type alias (document or element type). A rule keyed by a
	///     composition also applies to every type composed of it, where it hides only what the composition contributes.
	/// </summary>
	public Dictionary<string, ContentTypeVisibilityOptions> ContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Rules that several sites share, keyed by a free name. A site uses a set by naming it in
	///     <see cref="SiteVisibilityOptions.Include" />.
	/// </summary>
	public Dictionary<string, RuleSetOptions> RuleSets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Per-site rules keyed by a free label used in diagnostics.
	/// </summary>
	public Dictionary<string, SiteVisibilityOptions> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
