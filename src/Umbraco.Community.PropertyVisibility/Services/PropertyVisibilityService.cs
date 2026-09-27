using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.PropertyVisibility.Api.Models;
using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Default <see cref="IPropertyVisibilityService" />.
/// </summary>
/// <remarks>
///     Flow: options -> <see cref="PropertyVisibilityOptions.Enabled" /> -> content type (unknown: empty) -> root ->
///     site -> rule blocks (global plus site, keyed by the content type and by each type it is composed of) -> resolve.
///     The compositions come from the content type object itself, so a request looks up one content type, as before.
///     An unknown alias in a rule keyed by a composition is reported against that composition. Reading the options can
///     fail in two ways, both of which fail open with a <c>PV008</c> warning: validation (<see cref="OptionsValidationException" />) and binding (an
///     <see cref="InvalidOperationException" /> from the configuration binder for a value of the wrong type or an unknown
///     key). Each distinct failure is logged once; a successful read re-arms the log, so a later failure, or a different
///     one, is logged again. "No site matches root" (once per root key) and a site matching diagnostic such as name drift
///     (once per site) are logged at Debug only, both reset when the options change: the configuration analysis logger
///     reports them as warnings once per configuration change and again when a root node is created, renamed, moved,
///     trashed or deleted (<c>ConfigurationAnalysisTrigger</c>), and the health check shows them. Every other
///     failure returns an empty response and is logged: as an error the first time an exception of its type and message
///     occurs, at Debug for each repeat, until the options change. The response never carries a root node key or name, in <see cref="MatchedSiteModel" /> or in a warning:
///     any Content section user can call the API; the log and the health check carry those details.
/// </remarks>
public sealed class PropertyVisibilityService : IPropertyVisibilityService, IDisposable
{
	// Distinct unexpected failures remembered for log deduplication before the set starts over.
	private const int MaxDistinctRequestFailures = 64;

	private readonly IOptionsMonitor<PropertyVisibilityOptions> _options;
	private readonly IContentTypeService _contentTypeService;
	private readonly IRootNodeResolver _rootNodeResolver;
	private readonly ISiteMatcher _siteMatcher;
	private readonly IHiddenFieldsResolver _hiddenFieldsResolver;
	private readonly ILogger<PropertyVisibilityService> _logger;
	private readonly IDisposable? _changeSubscription;
	private readonly ConcurrentDictionary<Guid, byte> _unmatchedRootsLogged = new();
	private readonly ConcurrentDictionary<string, byte> _matchIssuesLogged = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _requestFailuresLogged = new(StringComparer.Ordinal);

	// The text of the last options failure that was logged; null while the options read fine.
	private string? _lastLoggedOptionsFailure;

	/// <summary>
	///     Initializes a new instance of the <see cref="PropertyVisibilityService" /> class.
	/// </summary>
	/// <param name="options">The options monitor; <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> is read per request.</param>
	/// <param name="contentTypeService">Resolves the content type by key (served from Umbraco's repository cache).</param>
	/// <param name="rootNodeResolver">Resolves the request to a root node.</param>
	/// <param name="siteMatcher">Matches the root to a configured site.</param>
	/// <param name="hiddenFieldsResolver">Turns rule blocks into keys.</param>
	/// <param name="logger">The logger.</param>
	public PropertyVisibilityService(
		IOptionsMonitor<PropertyVisibilityOptions> options,
		IContentTypeService contentTypeService,
		IRootNodeResolver rootNodeResolver,
		ISiteMatcher siteMatcher,
		IHiddenFieldsResolver hiddenFieldsResolver,
		ILogger<PropertyVisibilityService> logger)
	{
		_options = options;
		_contentTypeService = contentTypeService;
		_rootNodeResolver = rootNodeResolver;
		_siteMatcher = siteMatcher;
		_hiddenFieldsResolver = hiddenFieldsResolver;
		_logger = logger;
		// Only fires for a change that yields valid options (the monitor builds the new value before notifying);
		// the options failure log re-arms itself on the next successful read instead.
		_changeSubscription = options.OnChange(_ =>
		{
			_unmatchedRootsLogged.Clear();
			_matchIssuesLogged.Clear();
			_requestFailuresLogged.Clear();
		});
	}

	/// <inheritdoc />
	public HiddenFieldsResponseModel GetHiddenFields(Guid documentKey, Guid contentTypeKey, Guid? parentKey)
	{
		PropertyVisibilityOptions options;
		try
		{
			options = _options.CurrentValue;
		}
		catch (Exception ex)
		{
			LogOptionsFailureOnce(ex);
			return HiddenFieldsResponseModel.Empty(
				RootResolutionSource.None,
				[$"{IssueCodes.ConfigurationInvalid}: the PropertyVisibility configuration is invalid; nothing is hidden. See the server log for details."]);
		}

		if (Volatile.Read(ref _lastLoggedOptionsFailure) is not null)
		{
			Interlocked.Exchange(ref _lastLoggedOptionsFailure, null);
		}

		if (!options.Enabled)
		{
			return HiddenFieldsResponseModel.DisabledResponse();
		}

		try
		{
			return Compute(options, documentKey, contentTypeKey, parentKey);
		}
		catch (Exception ex)
		{
			LogRequestFailure(ex, documentKey, contentTypeKey);
			return HiddenFieldsResponseModel.Empty(RootResolutionSource.None, []);
		}
	}

	/// <inheritdoc />
	public void Dispose() => _changeSubscription?.Dispose();

	// The same failure repeats on every document open until its cause is fixed: the first of each kind is an error with
	// its stack trace, the repeats are Debug lines. The set is cleared when the options change, and when it grows past a
	// bound (failures whose message differs every time).
	private void LogRequestFailure(Exception ex, Guid documentKey, Guid contentTypeKey)
	{
		var signature = $"{ex.GetType().FullName}|{ex.Message}";
		if (_requestFailuresLogged.Count >= MaxDistinctRequestFailures)
		{
			_requestFailuresLogged.Clear();
		}

		if (_requestFailuresLogged.TryAdd(signature, 0))
		{
			_logger.LogError(
				ex,
				"PropertyVisibility failed for document {DocumentKey} of content type {ContentTypeKey}; nothing is hidden for this request. Repeats of this failure are logged at Debug level until the configuration changes.",
				documentKey,
				contentTypeKey);
			return;
		}

		_logger.LogDebug(
			"PropertyVisibility failed again for document {DocumentKey} of content type {ContentTypeKey} ({ExceptionType}: {ExceptionMessage}); nothing is hidden for this request.",
			documentKey,
			contentTypeKey,
			ex.GetType().FullName,
			ex.Message);
	}

	private void LogOptionsFailureOnce(Exception ex)
	{
		var failure = ex is OptionsValidationException validation
			? string.Join(" | ", validation.Failures)
			: ex.Message;

		if (string.Equals(Interlocked.Exchange(ref _lastLoggedOptionsFailure, failure), failure, StringComparison.Ordinal))
		{
			return;
		}

		_logger.LogError(
			ex,
			"{IssueCode}: the PropertyVisibility configuration is invalid; nothing is hidden until it is fixed. {Failure}",
			IssueCodes.ConfigurationInvalid,
			failure);
	}

	private HiddenFieldsResponseModel Compute(PropertyVisibilityOptions options, Guid documentKey, Guid contentTypeKey, Guid? parentKey)
	{
		IContentType? contentType = _contentTypeService.Get(contentTypeKey);
		if (contentType is null)
		{
			_logger.LogDebug("PropertyVisibility: content type {ContentTypeKey} not found; nothing is hidden.", contentTypeKey);
			return HiddenFieldsResponseModel.Empty(RootResolutionSource.None, [$"{IssueCodes.UnknownContentTypeKey}: content type {contentTypeKey} does not exist."]);
		}

		RootNodeResolution resolution = _rootNodeResolver.Resolve(documentKey, parentKey);
		SiteMatch match = _siteMatcher.Match(options, resolution);

		// The global and site blocks keyed by the content type, and by each type it is composed of (asked by the resolver).
		IReadOnlyList<ContentTypeVisibilityOptions> RuleBlocksFor(string alias)
		{
			var blocks = new List<ContentTypeVisibilityOptions>(2);
			if (options.ContentTypes.TryGetValueIgnoreCase(alias, out ContentTypeVisibilityOptions? globalBlock))
			{
				blocks.Add(globalBlock);
			}

			if (match.Site is not null && match.Site.ContentTypes.TryGetValueIgnoreCase(alias, out ContentTypeVisibilityOptions? siteBlock))
			{
				blocks.Add(siteBlock);
			}

			return blocks;
		}

		HiddenFields hidden = _hiddenFieldsResolver.Resolve(contentType, RuleBlocksFor, options.HideEmptiedContainers);

		var warnings = new List<string>();
		foreach (ConfigurationIssue issue in match.Issues)
		{
			warnings.Add(ToResponseWarning(issue, match.Label));
			if (_matchIssuesLogged.TryAdd($"{issue.Code}|{match.Label}", 0))
			{
				_logger.LogDebug("PropertyVisibility: {Issue}", issue.ToString());
			}
		}

		if (match.Reason == SiteMatchReason.None && options.Sites.Count > 0 && resolution.Source != RootResolutionSource.RecycleBin)
		{
			warnings.Add(resolution.RootKey is not null
				? $"{IssueCodes.RootWithoutSite}: no site rule matches this document's root and no site is the default; only global rules apply."
				: $"{IssueCodes.RootWithoutSite}: no root node could be resolved and no site is the default; only global rules apply.");

			if (resolution.RootKey is { } rootKey && _unmatchedRootsLogged.TryAdd(rootKey, 0))
			{
				_logger.LogDebug(
					"PropertyVisibility: no site matches root {RootKey} and no site is the default; only global rules apply. Add a RootNodeKey, a RootNodeName or an IsDefault site.",
					rootKey);
			}
		}

		// An alias is reported against the type its rule is keyed by: the content type, or the composition.
		AddUnmatchedWarnings(warnings, contentType, hidden.UnmatchedPropertyAliases, hidden.UnmatchedContainerAliases);
		foreach (UnmatchedCompositionAliases unmatched in hidden.UnmatchedOnCompositions)
		{
			AddUnmatchedWarnings(warnings, unmatched.Composition, unmatched.PropertyAliases, unmatched.ContainerAliases);
		}

		return new HiddenFieldsResponseModel
		{
			PropertyTypeKeys = hidden.PropertyTypeKeys.ToArray(),
			ContainerKeys = hidden.ContainerKeys.ToArray(),
			RootResolution = resolution.Source,
			MatchedSite = match.Site is null || match.Label is null
				? null
				: new MatchedSiteModel
				{
					Label = match.Label,
					Reason = match.Reason,
				},
			Warnings = warnings,
		};
	}

	private static void AddUnmatchedWarnings(
		List<string> warnings,
		IContentTypeComposition keyedBy,
		IReadOnlyList<string> propertyAliases,
		IReadOnlyList<string> containerAliases)
	{
		foreach (var alias in propertyAliases)
		{
			warnings.Add($"{IssueCodes.UnknownPropertyAlias}: property alias '{alias}' does not exist on content type '{keyedBy.Alias}'.");
		}

		if (containerAliases.Count == 0)
		{
			return;
		}

		var available = string.Join(", ", HiddenFieldsResolver.ConfigurableContainerAliases(keyedBy));

		foreach (var alias in containerAliases)
		{
			warnings.Add($"{IssueCodes.UnknownContainerAlias}: container alias '{alias}' does not exist on content type '{keyedBy.Alias}'. Available: {(available.Length == 0 ? "none" : available)}.");
		}
	}

	// The response goes to every Content section user, so a matching diagnostic is reduced to text without root keys or
	// names; the full issue goes to the server log (once per site and configuration) and to the health check.
	private static string ToResponseWarning(ConfigurationIssue issue, string? label) => issue.Code switch
	{
		IssueCodes.RootNodeNameDrift =>
			$"{issue.Code}: site '{label}' matched by RootNodeKey, but its configured RootNodeName differs from the root's current name; update or remove RootNodeName.",
		_ => $"{issue.Code}: see the server log or the Property Visibility health check for details.",
	};
}
