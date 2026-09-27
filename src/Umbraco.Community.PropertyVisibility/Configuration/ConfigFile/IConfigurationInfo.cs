namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Where the rules in effect come from.
/// </summary>
public enum ConfigurationSource
{
	/// <summary>
	///     No source: the options were not built yet, or the rules file exists but has never parsed, so it contributes
	///     no rules (and replaces the appsettings rules with nothing).
	/// </summary>
	None,

	/// <summary>The appsettings <c>PropertyVisibility</c> section: the file source is off, or the file does not exist or holds no JSON value.</summary>
	Appsettings,

	/// <summary>The rules file: its current content, or its last valid content when the current one does not parse.</summary>
	File,
}

/// <summary>
///     One consistent view of how the configuration was last loaded.
/// </summary>
/// <param name="ActiveSource">Where the rules of the latest load come from.</param>
/// <param name="FilePath">Full path of the rules file of the latest load; <c>null</c> when the file source is off.</param>
/// <param name="LoadedAt">
///     When the options were last built with rules that pass validation (UTC): the last successful load. A load whose
///     rules fail validation (the options then fail open) keeps the previous value. <c>null</c> before the first
///     successful load.
/// </param>
/// <param name="RulesHash">
///     Lower-case hex SHA-256 of the effective rules (<c>HideEmptiedContainers</c>, <c>ContentTypes</c>, <c>Sites</c>) of
///     the last successful load, in a canonical form: formatting, key order and list order do not change it. <c>null</c>
///     before the first successful load.
/// </param>
/// <param name="Issues">
///     Problems found by the latest load: <see cref="IssueCodes.InvalidJson" />, <see cref="IssueCodes.UnknownKey" /> and
///     <see cref="IssueCodes.BothSourcesDefineRules" />, plus <see cref="IssueCodes.ConfigFileNotWatched" /> while the rules
///     file cannot be watched. Structural validation issues are not included; see
///     <see cref="PropertyVisibilityOptionsValidator.Collect" />.
/// </param>
public sealed record ConfigurationState(
	ConfigurationSource ActiveSource,
	string? FilePath,
	DateTimeOffset? LoadedAt,
	string? RulesHash,
	IReadOnlyList<ConfigurationIssue> Issues)
{
	/// <summary>
	///     The state before the options were built for the first time.
	/// </summary>
	public static readonly ConfigurationState NotLoaded = new(ConfigurationSource.None, null, null, null, []);
}

/// <summary>
///     Describes the last configuration load, for the health check and diagnostics.
/// </summary>
/// <remarks>
///     Updated each time <c>IOptionsMonitor&lt;PropertyVisibilityOptions&gt;</c> builds the options (first read, and every
///     change of appsettings or the rules file), so read <c>CurrentValue</c> first when a fresh view is needed. A build
///     that fails in the configuration binder (<see cref="IssueCodes.ConfigurationInvalid" />) leaves the previous state
///     in place; a build whose rules fail validation updates the source, path and issues but keeps
///     <see cref="LoadedAt" /> and <see cref="RulesHash" /> of the last successful load. The individual properties each
///     read the latest state; use <see cref="Current" /> when several values must come from the same load.
/// </remarks>
public interface IConfigurationInfo
{
	/// <summary>The latest state as one consistent snapshot.</summary>
	ConfigurationState Current { get; }

	/// <summary>Where the rules in effect come from.</summary>
	ConfigurationSource ActiveSource { get; }

	/// <summary>Full path of the rules file; <c>null</c> when the file source is off.</summary>
	string? FilePath { get; }

	/// <summary>When the options last loaded with rules that pass validation (UTC); <c>null</c> before the first successful load.</summary>
	DateTimeOffset? LoadedAt { get; }

	/// <summary>Lower-case hex SHA-256 of the rules of the last successful load; <c>null</c> before the first successful load.</summary>
	string? RulesHash { get; }

	/// <summary>Problems found by the latest load (<c>PV001</c>, <c>PV002</c>, <c>PV301</c>), plus <c>PV304</c> while the rules file is not watched.</summary>
	IReadOnlyList<ConfigurationIssue> Issues { get; }
}
