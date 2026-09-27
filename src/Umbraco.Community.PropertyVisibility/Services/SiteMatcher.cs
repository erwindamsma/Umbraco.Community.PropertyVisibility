using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Default <see cref="ISiteMatcher" />: key, then name, then default.
/// </summary>
/// <remarks>
///     A site configured with both a key and a name is matched by key only, as long as the key is a root node: its name is
///     then used for drift detection alone (a <see cref="IssueCodes.RootNodeNameDrift" /> warning on the match when the
///     root's current name differs), so a stale name that equals another root's name never pulls that root into the site.
///     When the key is not a root node (<see cref="IssueCodes.RootNodeKeyNotARoot" />) the name still matches. Root names
///     are only read when a name is needed, and the resolver caches them.
/// </remarks>
public sealed class SiteMatcher : ISiteMatcher
{
	private readonly IRootNodeResolver _rootNodeResolver;

	/// <summary>
	///     Initializes a new instance of the <see cref="SiteMatcher" /> class.
	/// </summary>
	/// <param name="rootNodeResolver">Used to read root names for tier 2 and for drift detection.</param>
	public SiteMatcher(IRootNodeResolver rootNodeResolver) => _rootNodeResolver = rootNodeResolver;

	/// <inheritdoc />
	public SiteMatch Match(PropertyVisibilityOptions options, RootNodeResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(resolution);

		if (options.Sites.Count == 0 || resolution.Source == RootResolutionSource.RecycleBin)
		{
			return SiteMatch.None;
		}

		if (resolution.RootKey is { } rootKey)
		{
			// Tier 1: key.
			foreach ((var label, SiteVisibilityOptions site) in options.Sites)
			{
				if (site.RootNodeKey == rootKey)
				{
					return new SiteMatch(label, site, SiteMatchReason.Key, DetectDrift(label, site, rootKey));
				}
			}

			// Tier 2: name, only when some site is configured by name.
			if (options.Sites.Values.Any(site => !string.IsNullOrWhiteSpace(site.RootNodeName)))
			{
				var rootName = _rootNodeResolver.GetRootName(rootKey)?.Trim();
				if (!string.IsNullOrEmpty(rootName))
				{
					foreach ((var label, SiteVisibilityOptions site) in options.Sites)
					{
						if (!string.Equals(site.RootNodeName?.Trim(), rootName, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						// A site whose RootNodeKey is a root node belongs to that root only; its name is for drift detection.
						if (site.RootNodeKey is { } siteKey && siteKey != Guid.Empty && _rootNodeResolver.IsRoot(siteKey))
						{
							continue;
						}

						return new SiteMatch(label, site, SiteMatchReason.Name, []);
					}
				}
			}
		}

		// Tier 3: default.
		foreach ((var label, SiteVisibilityOptions site) in options.Sites)
		{
			if (site.IsDefault)
			{
				return new SiteMatch(label, site, SiteMatchReason.Default, []);
			}
		}

		return SiteMatch.None;
	}

	private IReadOnlyList<ConfigurationIssue> DetectDrift(string label, SiteVisibilityOptions site, Guid rootKey)
	{
		var configuredName = site.RootNodeName?.Trim();
		if (string.IsNullOrEmpty(configuredName))
		{
			return [];
		}

		var actualName = _rootNodeResolver.GetRootName(rootKey)?.Trim();
		if (string.Equals(configuredName, actualName, StringComparison.OrdinalIgnoreCase))
		{
			return [];
		}

		return
		[
			new ConfigurationIssue(
				IssueCodes.RootNodeNameDrift,
				IssueSeverity.Warning,
				$"Site '{label}' matched root {rootKey} by key, but its RootNodeName '{configuredName}' differs from the root's current name '{actualName}'.",
				$"Sites:{label}:RootNodeName"),
		];
	}
}
