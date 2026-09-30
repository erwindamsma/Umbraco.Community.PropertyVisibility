namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Resolves a site's <see cref="SiteVisibilityOptions.Include" /> to the rule sets it names.
/// </summary>
internal static class IncludedRuleSets
{
	/// <summary>
	///     Returns the rule sets a site includes, each once, in the order of its <see cref="SiteVisibilityOptions.Include" />.
	///     Names are matched case-insensitively. A name that matches no rule set is skipped: the validator rejects it
	///     (<see cref="IssueCodes.UnknownRuleSet" />), so valid options never have one.
	/// </summary>
	/// <param name="options">The options that hold the rule sets.</param>
	/// <param name="site">The site; <c>null</c> when no site matched.</param>
	/// <returns>The included rule sets; empty when the site includes none.</returns>
	public static IReadOnlyList<RuleSetOptions> Of(PropertyVisibilityOptions options, SiteVisibilityOptions? site)
	{
		if (site?.Include is not { Count: > 0 } names || options.RuleSets is not { Count: > 0 } ruleSets)
		{
			return [];
		}

		var included = new List<RuleSetOptions>(names.Count);
		foreach (var name in names)
		{
			if (name is not null
				&& ruleSets.TryGetValueIgnoreCase(name, out RuleSetOptions? ruleSet)
				&& ruleSet is not null
				&& !included.Contains(ruleSet))
			{
				included.Add(ruleSet);
			}
		}

		return included;
	}
}
