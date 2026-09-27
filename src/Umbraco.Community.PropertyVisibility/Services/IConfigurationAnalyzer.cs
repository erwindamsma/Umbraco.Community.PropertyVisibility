using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Checks the effective configuration against the running site: its root nodes, content types, properties and
///     containers, where the rules come from, and the Umbraco version.
/// </summary>
/// <remarks>
///     Used by the health check (on demand) and by the configuration analysis logger (once per configuration change).
///     Never used per request. Unlike the hidden-fields API, the result names root node keys and root node names; the
///     health check shows it to every user with access to the Settings section.
/// </remarks>
public interface IConfigurationAnalyzer
{
	/// <summary>
	///     Analyzes the current configuration. Reads the options first (which builds them when needed), then the load
	///     state of <see cref="IConfigurationInfo" />.
	/// </summary>
	/// <returns>The issues found and a summary of the configuration; never <c>null</c>.</returns>
	ConfigurationAnalysis Analyze();
}

/// <summary>
///     Result of <see cref="IConfigurationAnalyzer.Analyze" />.
/// </summary>
/// <param name="Summary">Where the rules come from and what they were checked against.</param>
/// <param name="Issues">Every issue found, in configuration order; empty when the configuration is clean.</param>
public sealed record ConfigurationAnalysis(ConfigurationAnalysisSummary Summary, IReadOnlyList<ConfigurationAnalysisIssue> Issues)
{
	/// <summary>Gets the number of issues with <see cref="IssueSeverity.Error" />.</summary>
	public int ErrorCount => Issues.Count(issue => issue.Severity == IssueSeverity.Error);

	/// <summary>Gets the number of issues with <see cref="IssueSeverity.Warning" />.</summary>
	public int WarningCount => Issues.Count(issue => issue.Severity == IssueSeverity.Warning);

	/// <summary>Gets the number of issues with <see cref="IssueSeverity.Info" />.</summary>
	public int InfoCount => Issues.Count(issue => issue.Severity == IssueSeverity.Info);

	/// <summary>Gets a value indicating whether the analysis found no errors and no warnings (informational issues allowed).</summary>
	public bool IsHealthy => Issues.All(issue => issue.Severity == IssueSeverity.Info);
}

/// <summary>
///     Summary of the configuration an analysis ran against.
/// </summary>
/// <param name="OptionsValid">
///     <c>false</c> when the options could not be bound or failed validation (<see cref="IssueCodes.ConfigurationInvalid" />);
///     the load state fields then describe the last successful load.
/// </param>
/// <param name="Enabled">The kill switch; <c>null</c> when the options could not be read.</param>
/// <param name="ActiveSource">Where the rules in effect come from.</param>
/// <param name="FilePath">Full path of the rules file; <c>null</c> when the file source is off.</param>
/// <param name="LoadedAt">When the options last loaded with rules that pass validation (UTC); <c>null</c> before the first successful load.</param>
/// <param name="RulesHash">Lower-case hex SHA-256 of the rules of the last successful load; <c>null</c> before the first successful load.</param>
/// <param name="SiteCount">Number of configured sites; 0 when the options could not be read.</param>
/// <param name="RootNodeKeys">Keys of the content root nodes, in tree order.</param>
/// <param name="UmbracoVersion">The running Umbraco version (major, minor, patch).</param>
/// <param name="TestedUmbracoVersions">The Umbraco versions the package's acceptance checklist was run against.</param>
public sealed record ConfigurationAnalysisSummary(
	bool OptionsValid,
	bool? Enabled,
	ConfigurationSource ActiveSource,
	string? FilePath,
	DateTimeOffset? LoadedAt,
	string? RulesHash,
	int SiteCount,
	IReadOnlyList<Guid> RootNodeKeys,
	Version UmbracoVersion,
	IReadOnlyList<Version> TestedUmbracoVersions)
{
	/// <summary>Gets a value indicating whether the running Umbraco version is in <see cref="TestedUmbracoVersions" />.</summary>
	public bool IsTestedUmbracoVersion => TestedUmbracoVersions.Contains(UmbracoVersion);
}

/// <summary>
///     One issue found by <see cref="IConfigurationAnalyzer" />, identified by a stable <see cref="IssueCodes" /> code.
/// </summary>
/// <param name="Code">The <see cref="IssueCodes" /> code.</param>
/// <param name="Severity">How serious the issue is.</param>
/// <param name="Message">Human-readable description, without the code and without the suggestion.</param>
/// <param name="SiteLabel">The site the issue belongs to; <c>null</c> for global rules and site-independent issues.</param>
/// <param name="ContentTypeAlias">The content type alias the issue belongs to, as configured; <c>null</c> when not about a content type.</param>
/// <param name="Suggestion">Optional did-you-mean value: the closest known alias, root name or root key.</param>
/// <param name="Path">
///     Optional location: a configuration path such as <c>Sites:corporate:ContentTypes:landingPage:Containers</c>, or a
///     JSON path in the rules file such as <c>$.Sites.corporate.RootNodeNme</c>.
/// </param>
public sealed record ConfigurationAnalysisIssue(
	string Code,
	IssueSeverity Severity,
	string Message,
	string? SiteLabel = null,
	string? ContentTypeAlias = null,
	string? Suggestion = null,
	string? Path = null)
{
	/// <summary>
	///     Gets the message followed by a <c>Did you mean '...'?</c> sentence when a suggestion is present.
	/// </summary>
	public string MessageWithSuggestion => Suggestion is null ? Message : $"{Message} Did you mean '{Suggestion}'?";

	/// <summary>
	///     Formats the issue as <c>CODE: message</c>, with the path in parentheses when present and a
	///     <c>Did you mean '...'?</c> sentence when a suggestion is present (the format of <see cref="ConfigurationIssue" />).
	/// </summary>
	/// <returns>The formatted issue.</returns>
	public override string ToString()
		=> Path is null ? $"{Code}: {MessageWithSuggestion}" : $"{Code} ({Path}): {MessageWithSuggestion}";
}
