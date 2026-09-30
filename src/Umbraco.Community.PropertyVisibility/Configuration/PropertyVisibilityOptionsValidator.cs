using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Validates the structural rules of <see cref="PropertyVisibilityOptions" />: every site has an identity,
///     identities are unique, at most one default, container aliases have a non-empty tab or group alias before any
///     <c>/</c> and a non-empty rest after it, site labels and rule set names carry no colon, and every rule set a site
///     includes exists.
/// </summary>
/// <remarks>
///     Everything that depends on the environment (unknown content types, unknown aliases, missing roots) is a
///     health check warning, not a validation failure, because content types differ per environment.
///     The validator is not wired to <c>ValidateOnStart</c>: a typo in a hot-reloadable file must not take the site down;
///     the service catches the <see cref="OptionsValidationException" /> (and any binding failure), logs it as
///     <see cref="IssueCodes.ConfigurationInvalid" /> and fails open instead.
/// </remarks>
public sealed partial class PropertyVisibilityOptionsValidator : IValidateOptions<PropertyVisibilityOptions>
{
	/// <summary>
	///     Collects every structural issue in <paramref name="options" />, errors and warnings alike.
	/// </summary>
	/// <param name="options">The options to inspect.</param>
	/// <returns>The issues found, in configuration order; empty when the options are structurally sound.</returns>
	public IReadOnlyList<ConfigurationIssue> Collect(PropertyVisibilityOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var issues = new List<ConfigurationIssue>();

		CollectContainerAliasIssues(options.ContentTypes, "ContentTypes", issues);

		foreach ((var name, RuleSetOptions? ruleSet) in options.RuleSets)
		{
			if (name.Contains(':'))
			{
				issues.Add(new ConfigurationIssue(
					IssueCodes.InvalidRuleSetName,
					IssueSeverity.Error,
					$"Rule set name '{name}' contains a colon; colons are reserved as configuration path separators.",
					$"RuleSets:{name}"));
			}

			CollectContainerAliasIssues(ruleSet?.ContentTypes, $"RuleSets:{name}:ContentTypes", issues);
		}

		var seenKeys = new Dictionary<Guid, string>();
		var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var defaults = new List<string>();

		foreach ((var label, SiteVisibilityOptions site) in options.Sites)
		{
			var path = $"Sites:{label}";

			if (label.Contains(':'))
			{
				issues.Add(new ConfigurationIssue(
					IssueCodes.InvalidSiteLabel,
					IssueSeverity.Error,
					$"Site label '{label}' contains a colon; colons are reserved as configuration path separators.",
					path));
			}

			var hasKey = site.RootNodeKey is { } key && key != Guid.Empty;
			var name = site.RootNodeName?.Trim();
			var hasName = !string.IsNullOrEmpty(name);

			if (!hasKey && !hasName && !site.IsDefault)
			{
				issues.Add(new ConfigurationIssue(
					IssueCodes.SiteWithoutIdentity,
					IssueSeverity.Error,
					$"Site '{label}' has none of RootNodeKey, RootNodeName or IsDefault; it can never match a root node.",
					path));
			}

			if (hasKey)
			{
				if (seenKeys.TryGetValue(site.RootNodeKey!.Value, out var other))
				{
					issues.Add(new ConfigurationIssue(
						IssueCodes.DuplicateSiteIdentity,
						IssueSeverity.Error,
						$"Sites '{other}' and '{label}' both use RootNodeKey {site.RootNodeKey.Value}.",
						path));
				}
				else
				{
					seenKeys[site.RootNodeKey.Value] = label;
				}
			}

			if (hasName)
			{
				if (seenNames.TryGetValue(name!, out var other))
				{
					issues.Add(new ConfigurationIssue(
						IssueCodes.DuplicateSiteIdentity,
						IssueSeverity.Error,
						$"Sites '{other}' and '{label}' both use RootNodeName '{name}'.",
						path));
				}
				else
				{
					seenNames[name!] = label;
				}
			}

			if (site.IsDefault)
			{
				defaults.Add(label);
			}

			foreach (var included in site.Include ?? [])
			{
				if (included is null || !options.RuleSets.TryGetValueIgnoreCase(included, out _))
				{
					issues.Add(new ConfigurationIssue(
						IssueCodes.UnknownRuleSet,
						IssueSeverity.Error,
						string.IsNullOrWhiteSpace(included)
							? $"Site '{label}' has an empty rule set name in Include."
							: $"Site '{label}' includes the rule set '{included}', which does not exist under RuleSets.",
						$"{path}:Include",
						included is null ? null : ConfigurationAnalyzer.Suggest(included, options.RuleSets.Keys)));
				}
			}

			CollectContainerAliasIssues(site.ContentTypes, $"{path}:ContentTypes", issues);
		}

		if (defaults.Count > 1)
		{
			issues.Add(new ConfigurationIssue(
				IssueCodes.MultipleDefaultSites,
				IssueSeverity.Error,
				$"More than one site is marked IsDefault: {string.Join(", ", defaults.Select(d => $"'{d}'"))}. Only one default site is allowed.",
				"Sites"));
		}

		return issues;
	}

	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, PropertyVisibilityOptions options)
	{
		if (options is null)
		{
			return ValidateOptionsResult.Fail("PropertyVisibility options are null.");
		}

		var errors = Collect(options)
			.Where(issue => issue.Severity == IssueSeverity.Error)
			.Select(issue => issue.ToString())
			.ToList();

		return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
	}

	/// <summary>
	///     Checks whether a configured container alias has the supported shape: <c>tab</c> or <c>group</c> (no <c>/</c>),
	///     or <c>tab/group</c>, split at the first <c>/</c> into a non-empty tab alias and a non-empty rest.
	/// </summary>
	/// <remarks>
	///     The rest may contain <c>/</c> itself: Umbraco builds a group's alias from its tab's alias and its own name, so
	///     group "Image/Video" in tab "Media" has the alias <c>media/image/Video</c>. Umbraco's Management API splits such an
	///     alias at the first <c>/</c> as well.
	/// </remarks>
	/// <param name="alias">The alias as configured.</param>
	/// <returns><c>true</c> when the alias is well-formed.</returns>
	public static bool IsValidContainerAlias(string? alias)
		=> !string.IsNullOrWhiteSpace(alias) && ContainerAliasPattern().IsMatch(alias);

	private static void CollectContainerAliasIssues(
		Dictionary<string, ContentTypeVisibilityOptions>? contentTypes,
		string path,
		List<ConfigurationIssue> issues)
	{
		foreach ((var alias, ContentTypeVisibilityOptions? block) in contentTypes ?? [])
		{
			foreach (var container in block?.Containers ?? [])
			{
				if (!IsValidContainerAlias(container))
				{
					issues.Add(new ConfigurationIssue(
						IssueCodes.InvalidContainerAlias,
						IssueSeverity.Error,
						$"Container alias '{container}' on content type '{alias}' is not valid; use 'tab', 'group' or 'tab/group': a tab alias before the first '/' and a group alias after it, neither empty.",
						$"{path}:{alias}:Containers"));
				}
			}
		}
	}

	[GeneratedRegex("^[^/]+(?:/.+)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
	private static partial Regex ContainerAliasPattern();
}
