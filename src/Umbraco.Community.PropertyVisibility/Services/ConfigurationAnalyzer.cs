using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Default <see cref="IConfigurationAnalyzer" />.
/// </summary>
/// <remarks>
///     <para>
///         Order: the options (a binding or validation failure is <see cref="IssueCodes.ConfigurationInvalid" />, with the
///         validator's <c>PV003</c> to <c>PV007</c>, <c>PV009</c> and <c>PV010</c> lines; nothing else about the rules can
///         be checked then), the load issues of <see cref="IConfigurationInfo" /> (<c>PV001</c>, <c>PV002</c>, <c>PV301</c>),
///         the kill switch (<c>PV302</c>), the sites against the root nodes (<c>PV101</c>, <c>PV102</c>; a name whose root
///         another site holds by key is checked through the real <see cref="ISiteMatcher" /> too), every root node through
///         the real <see cref="ISiteMatcher" /> (<c>PV103</c>, <c>PV105</c>), rule sets no site includes (<c>PV106</c>),
///         the content type entries (<c>PV104</c>, <c>PV201</c>, <c>PV202</c>, <c>PV203</c>, the informational
///         <c>PV205</c> for an entry keyed by a composition and <c>PV206</c> for an entry that hides nothing; global
///         entries once, each rule set's entries once, site entries per site) and the Umbraco version (<c>PV303</c>).
///     </para>
///     <para>
///         Aliases are resolved exactly as a request resolves them: content type aliases case-insensitively and untrimmed,
///         property and container aliases through <see cref="IHiddenFieldsResolver" /> against the entry's own content type
///         (its compositions included), which is also the structure an entry keyed by a composition resolves against in
///         the types composed of it, so an alias the analyzer accepts is one that hides something. Root names come from
///         <see cref="IRootNodeResolver.GetRootName" />, the source the site matcher uses. Suggestions are the closest
///         candidate by case-insensitive edit distance, within a limit that grows with the length of the value (see
///         <see cref="Suggest" />), or a candidate that contains the value or is contained in it (3 characters or more).
///         The container aliases listed and suggested for <c>PV202</c> are only those a rule can use: the validator
///         accepts them and they match the container they name.
///     </para>
///     <para>
///         Reads navigation and content type data that Umbraco keeps in memory; root names are read once per root and
///         cached by the resolver. Meant for the health check and for one run per configuration change, never per request.
///     </para>
/// </remarks>
public sealed partial class ConfigurationAnalyzer : IConfigurationAnalyzer
{
	// The shortest value that counts as contained in a suggestion, and below which only an exact (case-insensitive) match
	// is suggested.
	private const int MinContainedLength = 3;

	// How the messages name the top-level ContentTypes entries.
	private const string GlobalScope = "the global rules";

	private readonly IOptionsMonitor<PropertyVisibilityOptions> _options;
	private readonly IConfigurationInfo _configurationInfo;
	private readonly IDocumentNavigationQueryService _navigation;
	private readonly IRootNodeResolver _rootNodeResolver;
	private readonly ISiteMatcher _siteMatcher;
	private readonly IContentTypeService _contentTypeService;
	private readonly IHiddenFieldsResolver _hiddenFieldsResolver;
	private readonly IUmbracoVersion _umbracoVersion;
	private readonly PropertyVisibilityOptionsValidator _validator = new();

	/// <summary>
	///     Initializes a new instance of the <see cref="ConfigurationAnalyzer" /> class.
	/// </summary>
	/// <param name="options">The options monitor; a read failure is reported as <see cref="IssueCodes.ConfigurationInvalid" />.</param>
	/// <param name="configurationInfo">Where the rules come from and the rules file's load issues.</param>
	/// <param name="navigation">The in-memory document tree, for the root node keys.</param>
	/// <param name="rootNodeResolver">Root names, and the root a non-root key belongs to.</param>
	/// <param name="siteMatcher">Matches each root node to a site exactly as a request would.</param>
	/// <param name="contentTypeService">The document and element types.</param>
	/// <param name="hiddenFieldsResolver">Resolves configured property and container aliases against a content type.</param>
	/// <param name="umbracoVersion">The running Umbraco version.</param>
	public ConfigurationAnalyzer(
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IConfigurationInfo configurationInfo,
		IDocumentNavigationQueryService navigation,
		IRootNodeResolver rootNodeResolver,
		ISiteMatcher siteMatcher,
		IContentTypeService contentTypeService,
		IHiddenFieldsResolver hiddenFieldsResolver,
		IUmbracoVersion umbracoVersion)
	{
		_options = options;
		_configurationInfo = configurationInfo;
		_navigation = navigation;
		_rootNodeResolver = rootNodeResolver;
		_siteMatcher = siteMatcher;
		_contentTypeService = contentTypeService;
		_hiddenFieldsResolver = hiddenFieldsResolver;
		_umbracoVersion = umbracoVersion;
	}

	/// <inheritdoc />
	public ConfigurationAnalysis Analyze()
	{
		var issues = new List<ConfigurationAnalysisIssue>();

		// Options first: reading them builds them when needed, which refreshes the configuration info read next.
		PropertyVisibilityOptions? options = ReadOptions(issues);
		ConfigurationState state = _configurationInfo.Current;

		foreach (ConfigurationIssue loadIssue in state.Issues)
		{
			issues.Add(FromConfigurationIssue(loadIssue));
		}

		Guid[] roots = _navigation.TryGetRootKeys(out IEnumerable<Guid> rootKeys) ? rootKeys.ToArray() : [];
		var rootNames = new RootNames(roots, _rootNodeResolver);

		if (options is not null)
		{
			if (!options.Enabled)
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.Disabled,
					IssueSeverity.Info,
					"The package is disabled (Enabled is false): nothing is hidden. The rules are still checked.",
					Path: $"{PropertyVisibilityOptions.SectionName}:{nameof(PropertyVisibilityOptions.Enabled)}"));
			}

			// Validated options carry no structural errors; anything the validator reports below that level is kept.
			foreach (ConfigurationIssue structural in _validator.Collect(options))
			{
				issues.Add(FromConfigurationIssue(structural));
			}

			AnalyzeSites(options, rootNames, issues);
			AnalyzeRoots(options, rootNames, issues);
			AnalyzeRuleSets(options, issues);
			AnalyzeContentTypes(options, issues);
		}

		Version running = _umbracoVersion.Version;
		IReadOnlyList<Version> tested = Constants.TestedUmbracoVersions;
		if (!tested.Contains(running))
		{
			var sameMajor = running.Major == Constants.SupportedUmbracoMajor;
			issues.Add(new ConfigurationAnalysisIssue(
				IssueCodes.UntestedUmbracoVersion,
				sameMajor ? IssueSeverity.Info : IssueSeverity.Warning,
				sameMajor
					? $"Umbraco {running} is supported but not in the tested list ({string.Join(", ", tested)}). If tabs or groups are no longer hidden, see {Constants.CompatibilityDocumentationUrl}"
					: $"Umbraco {running} is not supported: the package supports Umbraco {Constants.SupportedUmbracoMajor}.x (tested: {string.Join(", ", tested)}), so tabs and groups may not be hidden. See {Constants.CompatibilityDocumentationUrl}",
				Path: "Umbraco"));
		}

		var summary = new ConfigurationAnalysisSummary(
			OptionsValid: options is not null,
			Enabled: options?.Enabled,
			ActiveSource: state.ActiveSource,
			FilePath: state.FilePath,
			LoadedAt: state.LoadedAt,
			RulesHash: state.RulesHash,
			SiteCount: options?.Sites.Count ?? 0,
			RootNodeKeys: roots,
			UmbracoVersion: running,
			TestedUmbracoVersions: tested);

		return new ConfigurationAnalysis(summary, issues);
	}

	/// <summary>
	///     Returns the candidate closest to <paramref name="value" />: the smallest case-insensitive edit distance (insertions,
	///     deletions, substitutions and swaps of two neighbouring characters each cost 1) among the candidates that are
	///     close, and the first candidate on a tie.
	/// </summary>
	/// <remarks>
	///     A candidate is close when its distance is within the limit for the value's length (after trimming): 0 below 3
	///     characters, 1 up to 5, 2 up to 8, then <c>max(3, length / 3)</c>; or when one of the two contains the other and
	///     the contained one has at least 3 characters. So <c>url</c> does not suggest <c>seo</c>, nor <c>menu</c>
	///     <c>meta</c>, nor <c>x</c> every alias with an x in it, while <c>titel</c> still suggests <c>title</c>.
	/// </remarks>
	/// <param name="value">The unknown value.</param>
	/// <param name="candidates">The known values.</param>
	/// <returns>The suggestion, or <c>null</c> when no candidate is close.</returns>
	internal static string? Suggest(string value, IEnumerable<string?> candidates)
	{
		var unknown = value.Trim();
		if (unknown.Length == 0)
		{
			return null;
		}

		var maxDistance = MaxSuggestionDistance(unknown.Length);
		var unknownLower = unknown.ToLowerInvariant();
		string? best = null;
		var bestDistance = int.MaxValue;
		foreach (var raw in candidates)
		{
			var candidate = raw?.Trim();
			if (string.IsNullOrEmpty(candidate))
			{
				continue;
			}

			var distance = EditDistance(unknownLower, candidate.ToLowerInvariant());
			var close = distance <= maxDistance || Contains(candidate, unknown) || Contains(unknown, candidate);
			if (close && distance < bestDistance)
			{
				best = candidate;
				bestDistance = distance;
			}
		}

		return best;
	}

	/// <summary>
	///     Optimal string alignment distance: the Levenshtein distance, where swapping two neighbouring characters also
	///     costs 1 (so <c>titel</c> is 1 from <c>title</c>, not 2).
	/// </summary>
	/// <param name="a">The first string.</param>
	/// <param name="b">The second string.</param>
	/// <returns>The distance.</returns>
	internal static int EditDistance(string a, string b)
	{
		var distances = new int[a.Length + 1, b.Length + 1];
		for (var i = 0; i <= a.Length; i++)
		{
			distances[i, 0] = i;
		}

		for (var j = 0; j <= b.Length; j++)
		{
			distances[0, j] = j;
		}

		for (var i = 1; i <= a.Length; i++)
		{
			for (var j = 1; j <= b.Length; j++)
			{
				var substitution = distances[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
				var distance = Math.Min(Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1), substitution);
				if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
				{
					distance = Math.Min(distance, distances[i - 2, j - 2] + 1);
				}

				distances[i, j] = distance;
			}
		}

		return distances[a.Length, b.Length];
	}

	private static int MaxSuggestionDistance(int length) => length switch
	{
		< MinContainedLength => 0,
		<= 5 => 1,
		<= 8 => 2,
		_ => Math.Max(3, length / 3),
	};

	// A short value is contained in too many aliases to be a useful hint ("x" is in "bodyText").
	private static bool Contains(string outer, string inner)
		=> inner.Length >= MinContainedLength && outer.Contains(inner, StringComparison.OrdinalIgnoreCase);

	private PropertyVisibilityOptions? ReadOptions(List<ConfigurationAnalysisIssue> issues)
	{
		try
		{
			return _options.CurrentValue;
		}
		catch (OptionsValidationException ex)
		{
			var details = new List<ConfigurationAnalysisIssue>();
			var unparsed = new List<string>();
			foreach (var failure in ex.Failures)
			{
				if (TryParseValidationFailure(failure, out ConfigurationAnalysisIssue? detail))
				{
					details.Add(detail);
				}
				else
				{
					unparsed.Add(failure);
				}
			}

			var message = "The configuration fails validation; nothing is hidden until it is fixed.";
			if (details.Count > 0)
			{
				message += $" {details.Count} validation {(details.Count == 1 ? "error is" : "errors are")} listed separately.";
			}

			if (unparsed.Count > 0)
			{
				message += " " + string.Join(" ", unparsed);
			}

			issues.Add(new ConfigurationAnalysisIssue(IssueCodes.ConfigurationInvalid, IssueSeverity.Error, message, Path: PropertyVisibilityOptions.SectionName));
			issues.AddRange(details);
			return null;
		}
		catch (Exception ex)
		{
			// The configuration binder (ErrorOnUnknownConfiguration): an unknown key, or a value of the wrong type.
			var reason = ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
			issues.Add(new ConfigurationAnalysisIssue(
				IssueCodes.ConfigurationInvalid,
				IssueSeverity.Error,
				$"The configuration cannot be read; nothing is hidden until it is fixed. {reason}",
				Path: PropertyVisibilityOptions.SectionName));
			return null;
		}
	}

	private void AnalyzeSites(PropertyVisibilityOptions options, RootNames rootNames, List<ConfigurationAnalysisIssue> issues)
	{
		foreach ((var label, SiteVisibilityOptions site) in options.Sites)
		{
			if (site is null)
			{
				continue;
			}

			var path = $"Sites:{label}";
			var name = site.RootNodeName?.Trim();
			var hasName = !string.IsNullOrEmpty(name);
			Guid? rootByName = hasName ? rootNames.FindByName(name!) : null;

			var hasKey = site.RootNodeKey is { } configuredKey && configuredKey != Guid.Empty;
			var keyIsRoot = hasKey && rootNames.Contains(site.RootNodeKey!.Value);

			// A site matched by key uses its name only for drift detection (PV105, reported per root below).
			if (keyIsRoot)
			{
				continue;
			}

			// The name's root can still belong to another site that holds its key: the matcher's key tier comes first.
			var claimedBy = rootByName is { } nameRoot ? ClaimedByAnotherSite(options, label, nameRoot) : null;

			if (hasKey)
			{
				issues.Add(RootKeyNotARoot(label, site, site.RootNodeKey!.Value, rootByName, claimedBy, rootNames, $"{path}:{nameof(SiteVisibilityOptions.RootNodeKey)}"));
			}

			if (!hasName)
			{
				continue;
			}

			var consequence = site.IsDefault
				? " The site still applies as the default site."
				: " The site's rules never apply to any root.";
			if (rootByName is null)
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.RootNodeNameNotFound,
					IssueSeverity.Warning,
					$"RootNodeName '{name}' of site '{label}' matches no root node.{consequence} {rootNames.Describe()}",
					SiteLabel: label,
					Suggestion: Suggest(name!, rootNames.Names),
					Path: $"{path}:{nameof(SiteVisibilityOptions.RootNodeName)}"));
			}
			else if (claimedBy is not null && !hasKey)
			{
				// With a RootNodeKey that is not a root, PV101 above already says the name's root is taken.
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.RootNodeNameNotFound,
					IssueSeverity.Warning,
					$"RootNodeName '{name}' of site '{label}' matches the root node {rootNames.Describe(rootByName.Value)}, but site '{claimedBy}' claims that root by RootNodeKey, so this site never applies to it.{consequence} Remove this site, or merge its rules into '{claimedBy}'.",
					SiteLabel: label,
					Path: $"{path}:{nameof(SiteVisibilityOptions.RootNodeName)}"));
			}
		}
	}

	// The label of the site that wins the root instead of the given one, or null when the given site wins it.
	private string? ClaimedByAnotherSite(PropertyVisibilityOptions options, string label, Guid root)
	{
		SiteMatch match = _siteMatcher.Match(options, new RootNodeResolution(root, RootResolutionSource.Document));
		return match.Label is not null && !string.Equals(match.Label, label, StringComparison.Ordinal) ? match.Label : null;
	}

	private ConfigurationAnalysisIssue RootKeyNotARoot(string label, SiteVisibilityOptions site, Guid key, Guid? rootByName, string? claimedBy, RootNames rootNames, string path)
	{
		RootNodeResolution resolution = _rootNodeResolver.Resolve(key, parentKey: null);
		string where;

		// Suggesting a root another site already holds by key would only produce a duplicate key.
		Guid? suggestion = claimedBy is null ? rootByName : null;
		if (resolution is { Source: RootResolutionSource.Document, RootKey: { } owningRoot })
		{
			where = $"it is a document under the root node {rootNames.Describe(owningRoot)}";
			suggestion ??= owningRoot;
		}
		else if (resolution.Source == RootResolutionSource.RecycleBin)
		{
			where = "it is a document in the recycle bin";
		}
		else
		{
			where = "no document with this key exists";
		}

		string consequence;
		if (rootByName is { } matchedByName && claimedBy is null)
		{
			consequence = $" The site still matches the root {rootNames.Describe(matchedByName)} by RootNodeName.";
		}
		else if (rootByName is { } takenByName)
		{
			consequence = $" Its RootNodeName matches the root {rootNames.Describe(takenByName)}, but site '{claimedBy}' claims that root by RootNodeKey."
				+ (site.IsDefault ? " The site still applies as the default site." : " The site never applies to any root.");
		}
		else if (site.IsDefault)
		{
			consequence = " The site still applies as the default site.";
		}
		else
		{
			consequence = " The site never matches by key.";
		}

		return new ConfigurationAnalysisIssue(
			IssueCodes.RootNodeKeyNotARoot,
			IssueSeverity.Warning,
			$"RootNodeKey {key} of site '{label}' is not a root node: {where}.{consequence} {rootNames.Describe()}",
			SiteLabel: label,
			Suggestion: suggestion?.ToString("D"),
			Path: path);
	}

	private void AnalyzeRoots(PropertyVisibilityOptions options, RootNames rootNames, List<ConfigurationAnalysisIssue> issues)
	{
		// Without sites every root gets the global rules by design (single-site setup); the matcher then matches nothing.
		if (options.Sites.Count == 0)
		{
			return;
		}

		foreach (Guid root in rootNames.Keys)
		{
			SiteMatch match = _siteMatcher.Match(options, new RootNodeResolution(root, RootResolutionSource.Document));

			foreach (ConfigurationIssue matchIssue in match.Issues)
			{
				var drift = matchIssue.Code == IssueCodes.RootNodeNameDrift;
				var suggestion = drift ? rootNames.NameOf(root)?.Trim() : matchIssue.Suggestion;
				issues.Add(new ConfigurationAnalysisIssue(
					matchIssue.Code,
					matchIssue.Severity,
					drift ? $"{matchIssue.Message}{DescribeDriftedName(match.Site, root, rootNames)} Update or remove RootNodeName." : matchIssue.Message,
					SiteLabel: match.Label,
					Suggestion: suggestion,
					Path: matchIssue.Path));
			}

			if (match.Reason == SiteMatchReason.None)
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.RootWithoutSite,
					IssueSeverity.Warning,
					$"The root node {rootNames.Describe(root)} matches no site and no site is the default; only the global rules apply to its documents. Add a site with this RootNodeKey; for a root that is not a site, such as a settings or shared content root, give that site no rules. A site marked IsDefault also ends this warning, but for every root no other site matches, roots added later included.",
					Path: nameof(PropertyVisibilityOptions.Sites)));
			}
		}
	}

	// A rule set that no site includes never applies. A set included only by a site that never matches is reported
	// through that site (PV101, PV102).
	private static void AnalyzeRuleSets(PropertyVisibilityOptions options, List<ConfigurationAnalysisIssue> issues)
	{
		foreach (var name in options.RuleSets.Keys)
		{
			var included = options.Sites.Values.Any(site =>
				site?.Include?.Any(include => string.Equals(include, name, StringComparison.OrdinalIgnoreCase)) == true);
			if (!included)
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.UnusedRuleSet,
					IssueSeverity.Warning,
					$"Rule set '{name}' is not included by any site, so its rules never apply. Add it to the Include of the sites that should use it, or remove it.",
					Path: $"{nameof(PropertyVisibilityOptions.RuleSets)}:{name}"));
			}
		}
	}

	// A stale name that is now another root's name: say that the site does not take that root (the matcher's key rule).
	private static string DescribeDriftedName(SiteVisibilityOptions? site, Guid root, RootNames rootNames)
	{
		var configured = site?.RootNodeName?.Trim();
		if (string.IsNullOrEmpty(configured) || rootNames.FindByName(configured) is not { } other || other == root)
		{
			return string.Empty;
		}

		return $" '{configured}' is the name of the root node {rootNames.Describe(other)}, which this site does not match: a site whose RootNodeKey is a root node is matched by that key only.";
	}

	private void AnalyzeContentTypes(PropertyVisibilityOptions options, List<ConfigurationAnalysisIssue> issues)
	{
		var anyEntries = options.ContentTypes.Count > 0
			|| options.RuleSets.Values.Any(ruleSet => ruleSet?.ContentTypes.Count > 0)
			|| options.Sites.Values.Any(site => site?.ContentTypes.Count > 0);
		if (!anyEntries)
		{
			return;
		}

		var contentTypes = new Dictionary<string, IContentType>(StringComparer.OrdinalIgnoreCase);

		// Composition alias -> aliases of the content types composed of it, directly or transitively.
		var composedOf = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (IContentType contentType in _contentTypeService.GetAll())
		{
			contentTypes.TryAdd(contentType.Alias, contentType);
			foreach (IContentTypeComposition composition in CompositionClosure.Of(contentType))
			{
				if (!composedOf.TryGetValue(composition.Alias, out SortedSet<string>? composers))
				{
					composers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
					composedOf[composition.Alias] = composers;
				}

				composers.Add(contentType.Alias);
			}
		}

		AnalyzeContentTypeEntries(options.ContentTypes, siteLabel: null, GlobalScope, nameof(PropertyVisibilityOptions.ContentTypes), contentTypes, composedOf, issues);

		foreach ((var name, RuleSetOptions ruleSet) in options.RuleSets)
		{
			if (ruleSet is not null)
			{
				var path = $"{nameof(PropertyVisibilityOptions.RuleSets)}:{name}:{nameof(RuleSetOptions.ContentTypes)}";
				AnalyzeContentTypeEntries(ruleSet.ContentTypes, siteLabel: null, $"rule set '{name}'", path, contentTypes, composedOf, issues);
			}
		}

		foreach ((var label, SiteVisibilityOptions site) in options.Sites)
		{
			if (site is not null)
			{
				var path = $"{nameof(PropertyVisibilityOptions.Sites)}:{label}:{nameof(SiteVisibilityOptions.ContentTypes)}";
				AnalyzeContentTypeEntries(site.ContentTypes, label, $"site '{label}'", path, contentTypes, composedOf, issues);
			}
		}
	}

	// Scope is how the messages name where the entries are: the global rules, "rule set 'x'" or "site 'x'".
	private void AnalyzeContentTypeEntries(
		Dictionary<string, ContentTypeVisibilityOptions> entries,
		string? siteLabel,
		string scope,
		string parentPath,
		Dictionary<string, IContentType> contentTypes,
		Dictionary<string, SortedSet<string>> composedOf,
		List<ConfigurationAnalysisIssue> issues)
	{
		foreach ((var alias, ContentTypeVisibilityOptions? block) in entries)
		{
			var path = $"{parentPath}:{alias}";

			// Untrimmed, case-insensitive: exactly how a request looks the entry up.
			if (!contentTypes.TryGetValue(alias, out IContentType? contentType))
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.UnknownContentTypeAlias,
					IssueSeverity.Warning,
					$"Content type '{alias}' in {scope} does not exist as a document type or element type; its rules never apply.",
					SiteLabel: siteLabel,
					ContentTypeAlias: alias,
					Suggestion: Suggest(alias, contentTypes.Keys),
					Path: path));
				continue;
			}

			// An entry without aliases hides nothing; the other checks have nothing to check.
			if (block is null || (block.Properties is not { Count: > 0 } && block.Containers is not { Count: > 0 }))
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.EmptyContentTypeRule,
					IssueSeverity.Info,
					$"Content type '{alias}' in {scope} lists no properties and no containers, so it hides nothing. Add aliases, or remove the entry.",
					SiteLabel: siteLabel,
					ContentTypeAlias: alias,
					Path: path));
				continue;
			}

			HiddenFields hidden = _hiddenFieldsResolver.Resolve(contentType, [block], hideEmptiedContainers: false);

			if (hidden.UnmatchedPropertyAliases.Count > 0)
			{
				var propertyAliases = contentType.CompositionPropertyTypes.Select(property => property.Alias).ToList();
				foreach (var property in hidden.UnmatchedPropertyAliases)
				{
					issues.Add(new ConfigurationAnalysisIssue(
						IssueCodes.UnknownPropertyAlias,
						IssueSeverity.Warning,
						$"Property '{property}' in {scope} does not exist on content type '{contentType.Alias}' (own properties and compositions checked).",
						SiteLabel: siteLabel,
						ContentTypeAlias: alias,
						Suggestion: Suggest(property, propertyAliases),
						Path: $"{path}:{nameof(ContentTypeVisibilityOptions.Properties)}"));
				}
			}

			if (hidden.UnmatchedContainerAliases.Count > 0)
			{
				// Only aliases a rule can use: listing or suggesting one the validator rejects would turn the whole
				// configuration invalid (PV006, then PV008) when applied.
				List<string> containerAliases = HiddenFieldsResolver.ConfigurableContainerAliases(contentType);
				var available = containerAliases.Count > 0
					? $"Available containers: {string.Join(", ", containerAliases)}."
					: contentType.CompositionPropertyGroups.Any()
						? "The content type has no tab or group whose alias a rule can name."
						: "The content type has no tabs or groups.";

				foreach (var container in hidden.UnmatchedContainerAliases)
				{
					issues.Add(new ConfigurationAnalysisIssue(
						IssueCodes.UnknownContainerAlias,
						IssueSeverity.Warning,
						$"Container '{container}' in {scope} does not exist on content type '{contentType.Alias}' (own containers and compositions checked). {available}",
						SiteLabel: siteLabel,
						ContentTypeAlias: alias,
						Suggestion: Suggest(container, containerAliases),
						Path: $"{path}:{nameof(ContentTypeVisibilityOptions.Containers)}"));
				}
			}

			foreach (IPropertyType property in contentType.CompositionPropertyTypes)
			{
				if (property.Mandatory && hidden.PropertyTypeKeys.Contains(property.Key))
				{
					issues.Add(new ConfigurationAnalysisIssue(
						IssueCodes.HiddenMandatoryProperty,
						IssueSeverity.Warning,
						$"Property '{property.Alias}' on content type '{contentType.Alias}' is mandatory and hidden by {scope}: editors cannot fill it in, and publishing still requires a value. Make it optional or stop hiding it.",
						SiteLabel: siteLabel,
						ContentTypeAlias: alias,
						Path: path));
				}
			}

			if (composedOf.TryGetValue(contentType.Alias, out SortedSet<string>? composers))
			{
				issues.Add(new ConfigurationAnalysisIssue(
					IssueCodes.CompositionRuleReach,
					IssueSeverity.Info,
					DescribeCompositionReach(contentType.Alias, scope, composers),
					SiteLabel: siteLabel,
					ContentTypeAlias: alias,
					Path: path));
			}
		}
	}

	private static string DescribeCompositionReach(string compositionAlias, string scope, SortedSet<string> composers)
	{
		const int listed = 10;
		var rules = scope == GlobalScope ? GlobalScope : $"the rules of {scope}";
		var types = composers.Count == 1 ? "1 content type" : $"{composers.Count} content types";
		var list = string.Join(", ", composers.Take(listed));
		if (composers.Count > listed)
		{
			list += $" and {composers.Count - listed} more";
		}

		return $"Content type '{compositionAlias}' is a composition, so {rules} for it also apply to the {types} composed of it: {list}. There they hide only the properties and containers '{compositionAlias}' contributes.";
	}

	private static ConfigurationAnalysisIssue FromConfigurationIssue(ConfigurationIssue issue)
	{
		(string? siteLabel, string? contentTypeAlias) = issue.Code == IssueCodes.InvalidSiteLabel ? (null, null) : LocateInPath(issue.Path);
		return new ConfigurationAnalysisIssue(issue.Code, issue.Severity, issue.Message, siteLabel, contentTypeAlias, issue.Suggestion, issue.Path);
	}

	private static bool TryParseValidationFailure(string failure, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ConfigurationAnalysisIssue? issue)
	{
		// The validator reports each failure as ConfigurationIssue.ToString(): "PV003 (Sites:corporate): message", followed
		// by " Did you mean 'x'?" when the issue has a suggestion (PV009).
		Match match = ValidationFailurePattern().Match(failure);
		if (!match.Success)
		{
			issue = null;
			return false;
		}

		var path = match.Groups["path"].Success ? match.Groups["path"].Value : null;
		var suggestion = match.Groups["suggestion"].Success ? match.Groups["suggestion"].Value : null;
		issue = FromConfigurationIssue(new ConfigurationIssue(match.Groups["code"].Value, IssueSeverity.Error, match.Groups["message"].Value, path, suggestion));
		return true;
	}

	private static (string? SiteLabel, string? ContentTypeAlias) LocateInPath(string? path)
	{
		if (string.IsNullOrEmpty(path) || path.StartsWith('$'))
		{
			return (null, null);
		}

		var segments = path.Split(':');
		string? siteLabel = segments.Length > 1 && segments[0] == nameof(PropertyVisibilityOptions.Sites) ? segments[1] : null;
		var index = Array.IndexOf(segments, nameof(PropertyVisibilityOptions.ContentTypes));
		string? contentTypeAlias = index >= 0 && index + 1 < segments.Length ? segments[index + 1] : null;
		return (siteLabel, contentTypeAlias);
	}

	// The path ends at the first "): ", so a site label with parentheses, such as "Corporate (old)", stays in the path.
	[GeneratedRegex(@"^(?<code>PV\d{3})(?: \((?<path>.*?)\))?: (?<message>.*?)(?: Did you mean '(?<suggestion>.*)'\?)?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
	private static partial Regex ValidationFailurePattern();

	/// <summary>
	///     The root node keys with their names, read once per analysis.
	/// </summary>
	private sealed class RootNames
	{
		private readonly IRootNodeResolver _resolver;
		private readonly HashSet<Guid> _keySet;
		private readonly Dictionary<Guid, string?> _names = [];

		public RootNames(IReadOnlyList<Guid> keys, IRootNodeResolver resolver)
		{
			Keys = keys;
			_keySet = [.. keys];
			_resolver = resolver;
		}

		public IReadOnlyList<Guid> Keys { get; }

		public IEnumerable<string?> Names => Keys.Select(NameOf);

		public bool Contains(Guid key) => _keySet.Contains(key);

		public string? NameOf(Guid key)
		{
			if (!_names.TryGetValue(key, out var name))
			{
				name = _resolver.GetRootName(key);
				_names[key] = name;
			}

			return name;
		}

		public Guid? FindByName(string name)
		{
			foreach (Guid key in Keys)
			{
				if (string.Equals(NameOf(key)?.Trim(), name, StringComparison.OrdinalIgnoreCase))
				{
					return key;
				}
			}

			return null;
		}

		public string Describe(Guid key) => NameOf(key) is { } name ? $"'{name}' ({key})" : $"{key} (name unavailable)";

		public string Describe()
			=> Keys.Count == 0
				? "There are no root nodes."
				: $"Root nodes: {string.Join(", ", Keys.Select(Describe))}.";
	}
}
