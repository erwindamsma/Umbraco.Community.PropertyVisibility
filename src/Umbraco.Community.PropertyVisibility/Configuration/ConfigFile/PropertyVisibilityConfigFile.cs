using System.Text.Json.Serialization;

namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Shape of the optional rules file named by <see cref="PropertyVisibilityOptions.ConfigFile" />
///     (<c>PropertyVisibility.config.json</c> next to appsettings.json by default).
/// </summary>
/// <remarks>
///     When the file exists and parses, its <see cref="ContentTypes" />, <see cref="RuleSets" /> and <see cref="Sites" />
///     replace the appsettings ones wholesale (no merge), and <see cref="HideEmptiedContainers" /> overrides the
///     appsettings value when set. <see cref="PropertyVisibilityOptions.Enabled" /> and
///     <see cref="PropertyVisibilityOptions.ConfigFile" /> are appsettings-only and are rejected here as unknown keys.
///     Keys are case-insensitive; comments and trailing commas are allowed.
/// </remarks>
public sealed class PropertyVisibilityConfigFile
{
	/// <summary>
	///     Optional reference to the JSON schema of this file, for editor IntelliSense. Ignored when the rules are loaded.
	/// </summary>
	[JsonPropertyName("$schema")]
	public string? Schema { get; set; }

	/// <summary>
	///     Overrides <c>PropertyVisibility:HideEmptiedContainers</c> from appsettings when set; when omitted the appsettings
	///     value applies.
	/// </summary>
	public bool? HideEmptiedContainers { get; set; }

	/// <summary>
	///     Rules that apply on every site, keyed by content type alias (document or element type). A rule keyed by a
	///     composition also applies to every type composed of it, where it hides only what the composition contributes.
	///     Replaces <c>PropertyVisibility:ContentTypes</c> from appsettings.
	/// </summary>
	public Dictionary<string, ContentTypeVisibilityOptions> ContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Rules that several sites share, keyed by a free name; a site uses a set by naming it in its <c>Include</c>.
	///     Replaces <c>PropertyVisibility:RuleSets</c> from appsettings.
	/// </summary>
	public Dictionary<string, RuleSetOptions> RuleSets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Per-site rules keyed by a free label used in diagnostics. Replaces <c>PropertyVisibility:Sites</c> from appsettings.
	/// </summary>
	public Dictionary<string, SiteVisibilityOptions> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
