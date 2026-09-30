namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     The rules of one successful parse of the rules file, kept as the last good version.
/// </summary>
/// <remarks>
///     Immutable after construction: <see cref="ApplyTo" /> hands every options instance its own deep copy with
///     case-insensitive dictionaries, so options built from the same parse never share mutable state. Null collections and
///     null entries in the file (<c>"Properties": null</c>, <c>"landingPage": null</c>) become empty ones.
/// </remarks>
internal sealed class ConfigFileRules
{
	private readonly Dictionary<string, ContentTypeVisibilityOptions> _contentTypes;
	private readonly Dictionary<string, RuleSetOptions> _ruleSets;
	private readonly Dictionary<string, SiteVisibilityOptions> _sites;

	private ConfigFileRules(
		bool? hideEmptiedContainers,
		Dictionary<string, ContentTypeVisibilityOptions> contentTypes,
		Dictionary<string, RuleSetOptions> ruleSets,
		Dictionary<string, SiteVisibilityOptions> sites)
	{
		HideEmptiedContainers = hideEmptiedContainers;
		_contentTypes = contentTypes;
		_ruleSets = ruleSets;
		_sites = sites;
	}

	/// <summary>No rules: what a rules file that never parsed contributes.</summary>
	public static ConfigFileRules Empty { get; } = new(
		null,
		new(StringComparer.OrdinalIgnoreCase),
		new(StringComparer.OrdinalIgnoreCase),
		new(StringComparer.OrdinalIgnoreCase));

	/// <summary>The file's <c>HideEmptiedContainers</c>, or <c>null</c> when the file does not set it.</summary>
	public bool? HideEmptiedContainers { get; }

	/// <summary>
	///     Copies the parsed file model.
	/// </summary>
	/// <param name="file">The deserialized file.</param>
	/// <returns>The rules.</returns>
	public static ConfigFileRules From(PropertyVisibilityConfigFile file)
		=> new(file.HideEmptiedContainers, CopyContentTypes(file.ContentTypes), CopyRuleSets(file.RuleSets), CopySites(file.Sites));

	/// <summary>
	///     Replaces <see cref="PropertyVisibilityOptions.ContentTypes" />, <see cref="PropertyVisibilityOptions.RuleSets" />
	///     and <see cref="PropertyVisibilityOptions.Sites" /> with copies of these rules, and
	///     <see cref="PropertyVisibilityOptions.HideEmptiedContainers" /> when the file set it.
	///     <see cref="PropertyVisibilityOptions.Enabled" /> and <see cref="PropertyVisibilityOptions.ConfigFile" /> are not touched.
	/// </summary>
	/// <param name="options">The options bound from appsettings.</param>
	public void ApplyTo(PropertyVisibilityOptions options)
	{
		options.ContentTypes = CopyContentTypes(_contentTypes);
		options.RuleSets = CopyRuleSets(_ruleSets);
		options.Sites = CopySites(_sites);
		if (HideEmptiedContainers is { } hideEmptiedContainers)
		{
			options.HideEmptiedContainers = hideEmptiedContainers;
		}
	}

	private static Dictionary<string, ContentTypeVisibilityOptions> CopyContentTypes(Dictionary<string, ContentTypeVisibilityOptions>? source)
	{
		var copy = new Dictionary<string, ContentTypeVisibilityOptions>(StringComparer.OrdinalIgnoreCase);
		foreach ((var alias, ContentTypeVisibilityOptions? block) in source ?? [])
		{
			copy[alias] = new ContentTypeVisibilityOptions
			{
				Properties = CopyAliases(block?.Properties),
				Containers = CopyAliases(block?.Containers),
			};
		}

		return copy;
	}

	private static Dictionary<string, RuleSetOptions> CopyRuleSets(Dictionary<string, RuleSetOptions>? source)
	{
		var copy = new Dictionary<string, RuleSetOptions>(StringComparer.OrdinalIgnoreCase);
		foreach ((var name, RuleSetOptions? ruleSet) in source ?? [])
		{
			copy[name] = new RuleSetOptions { ContentTypes = CopyContentTypes(ruleSet?.ContentTypes) };
		}

		return copy;
	}

	private static Dictionary<string, SiteVisibilityOptions> CopySites(Dictionary<string, SiteVisibilityOptions>? source)
	{
		var copy = new Dictionary<string, SiteVisibilityOptions>(StringComparer.OrdinalIgnoreCase);
		foreach ((var label, SiteVisibilityOptions? site) in source ?? [])
		{
			copy[label] = new SiteVisibilityOptions
			{
				RootNodeKey = site?.RootNodeKey,
				RootNodeName = site?.RootNodeName,
				IsDefault = site?.IsDefault ?? false,
				Include = CopyAliases(site?.Include),
				ContentTypes = CopyContentTypes(site?.ContentTypes),
			};
		}

		return copy;
	}

	private static List<string> CopyAliases(List<string>? source)
		=> source is null ? [] : source.Where(alias => alias is not null).ToList();
}
