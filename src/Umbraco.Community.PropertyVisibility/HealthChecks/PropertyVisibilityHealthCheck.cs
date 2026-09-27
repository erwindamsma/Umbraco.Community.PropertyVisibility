using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.HealthChecks;

/// <summary>
///     Settings > Health Check > Configuration: runs <see cref="IConfigurationAnalyzer" /> and shows one status per issue
///     plus a summary with the rules source, file path, load time, rules hash and the Umbraco version against the tested list.
/// </summary>
/// <remarks>
///     <para>
///         Umbraco discovers health checks by type scanning (<c>TypeLoader.GetTypes&lt;HealthCheck&gt;()</c>, the same scan
///         that finds the package composer), so no registration is needed; the instance is created through dependency
///         injection each time the checks run.
///     </para>
///     <para>
///         Result types: an <see cref="IssueSeverity.Error" /> issue is <see cref="StatusResultType.Error" />, a
///         <see cref="IssueSeverity.Warning" /> is <see cref="StatusResultType.Warning" />, an <see cref="IssueSeverity.Info" />
///         is <see cref="StatusResultType.Info" />. The summary is <see cref="StatusResultType.Success" /> when there is no
///         error and no warning, otherwise <see cref="StatusResultType.Info" />, so each problem is counted once in the
///         dashboard's totals. The summary comes first, then the issues: errors before warnings before information, each
///         group in configuration order.
///     </para>
///     <para>
///         The backoffice renders a status message as HTML, and root names are typed by editors, so every value in a message
///         is HTML-encoded.
///     </para>
/// </remarks>
[HealthCheck(
	"b35b2a1d-1954-42f2-a2ec-73f7d2f366b4",
	"Property Visibility configuration",
	Description = "Checks the Property Visibility rules against this site's root nodes, document types and element types, and shows where the rules come from.",
	Group = "Configuration")]
public sealed class PropertyVisibilityHealthCheck : HealthCheck
{
	/// <summary>
	///     The "Read more" link of every issue status: the issue code reference.
	/// </summary>
	public const string IssueCodesDocumentationUrl = "https://github.com/erwindamsma/Umbraco.Community.PropertyVisibility/blob/main/docs/configuration.md#issue-codes";

	private readonly IConfigurationAnalyzer _analyzer;
	private readonly ILogger<PropertyVisibilityHealthCheck> _logger;

	/// <summary>
	///     Initializes a new instance of the <see cref="PropertyVisibilityHealthCheck" /> class.
	/// </summary>
	/// <param name="analyzer">Produces the issues and the summary.</param>
	/// <param name="logger">Logs an analysis that fails.</param>
	public PropertyVisibilityHealthCheck(IConfigurationAnalyzer analyzer, ILogger<PropertyVisibilityHealthCheck> logger)
	{
		_analyzer = analyzer;
		_logger = logger;
	}

	/// <inheritdoc />
	public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
		=> Task.FromResult<IEnumerable<HealthCheckStatus>>(GetStatuses());

	/// <summary>
	///     Maps an issue severity to the health check result type.
	/// </summary>
	/// <param name="severity">The severity.</param>
	/// <returns><see cref="StatusResultType.Error" />, <see cref="StatusResultType.Warning" /> or <see cref="StatusResultType.Info" />.</returns>
	public static StatusResultType ToResultType(IssueSeverity severity) => severity switch
	{
		IssueSeverity.Error => StatusResultType.Error,
		IssueSeverity.Warning => StatusResultType.Warning,
		_ => StatusResultType.Info,
	};

	/// <summary>
	///     Runs the analysis and builds the statuses: the summary, then the issues (most severe first).
	/// </summary>
	/// <returns>The statuses; a single <see cref="StatusResultType.Error" /> status when the analysis itself fails.</returns>
	internal IReadOnlyList<HealthCheckStatus> GetStatuses()
	{
		ConfigurationAnalysis analysis;
		try
		{
			analysis = _analyzer.Analyze();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "PropertyVisibility: the configuration analysis of the health check failed.");
			return
			[
				new HealthCheckStatus($"The Property Visibility configuration could not be analyzed: {Encode(ex.Message)}. See the server log for details.")
				{
					ResultType = StatusResultType.Error,
				},
			];
		}

		var statuses = new List<HealthCheckStatus> { Summary(analysis) };
		statuses.AddRange(analysis.Issues
			.OrderBy(issue => SeverityRank(issue.Severity))
			.Select(ToStatus));
		return statuses;
	}

	private static int SeverityRank(IssueSeverity severity) => severity switch
	{
		IssueSeverity.Error => 0,
		IssueSeverity.Warning => 1,
		_ => 2,
	};

	private static HealthCheckStatus ToStatus(ConfigurationAnalysisIssue issue)
	{
		var message = new StringBuilder();
		message.Append("<strong>").Append(Encode(issue.Code)).Append("</strong> ");
		if (issue.Path is not null)
		{
			message.Append("<code>").Append(Encode(issue.Path)).Append("</code>: ");
		}

		message.Append(Encode(issue.Message));
		if (issue.Suggestion is not null)
		{
			message.Append(" Did you mean <code>").Append(Encode(issue.Suggestion)).Append("</code>?");
		}

		return new HealthCheckStatus(message.ToString())
		{
			ResultType = ToResultType(issue.Severity),
			ReadMoreLink = IssueCodesDocumentationUrl,
		};
	}

	private static HealthCheckStatus Summary(ConfigurationAnalysis analysis)
	{
		ConfigurationAnalysisSummary summary = analysis.Summary;
		var lines = new List<string>();

		// Invalid options always carry a PV008 error, so a healthy analysis always had valid options.
		if (analysis.IsHealthy)
		{
			lines.Add("The Property Visibility configuration is valid: every configured root node, content type, property and container exists.");
		}
		else
		{
			lines.Add(Encode($"The Property Visibility configuration has {Count(analysis.ErrorCount, "error")} and {Count(analysis.WarningCount, "warning")}; see below."));
		}

		lines.Add(Encode(DescribeSource(summary)));

		// LoadedAt and RulesHash describe the last load whose rules passed validation, never a rejected one.
		if (summary.LoadedAt is { } loadedAt)
		{
			var loaded = loadedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
			lines.Add(summary.OptionsValid
				? $"Loaded at {Encode(loaded)}; rules hash <code>{Encode(summary.RulesHash ?? "-")}</code>."
				: $"Last successful load at {Encode(loaded)}; rules hash <code>{Encode(summary.RulesHash ?? "-")}</code>.");
		}
		else if (!summary.OptionsValid)
		{
			lines.Add("No configuration has loaded successfully since the site started.");
		}

		if (summary.OptionsValid)
		{
			var enabled = summary.Enabled == false ? " The package is disabled." : string.Empty;
			lines.Add(Encode($"{Count(summary.SiteCount, "site")} configured, {Count(summary.RootNodeKeys.Count, "root node")} in the content tree.{enabled}"));
		}

		var tested = string.Join(", ", summary.TestedUmbracoVersions);
		lines.Add(Encode(summary.IsTestedUmbracoVersion
			? $"Umbraco {summary.UmbracoVersion} (tested: {tested})."
			: $"Umbraco {summary.UmbracoVersion} is not in the tested list (tested: {tested})."));

		return new HealthCheckStatus(string.Join("<br>", lines))
		{
			ResultType = analysis.IsHealthy ? StatusResultType.Success : StatusResultType.Info,
		};
	}

	private static string DescribeSource(ConfigurationAnalysisSummary summary) => summary.ActiveSource switch
	{
		ConfigurationSource.File => $"Rules source: the rules file {summary.FilePath}.",
		ConfigurationSource.Appsettings when summary.FilePath is null => "Rules source: appsettings (the rules file is turned off).",
		ConfigurationSource.Appsettings => $"Rules source: appsettings (the rules file {summary.FilePath} does not exist or holds no JSON value).",
		_ when summary.LoadedAt is null => "Rules source: not loaded yet.",
		_ => $"Rules source: none. The rules file {summary.FilePath} has never loaded, so no rules apply (the file still replaces the appsettings rules).",
	};

	private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

	private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
