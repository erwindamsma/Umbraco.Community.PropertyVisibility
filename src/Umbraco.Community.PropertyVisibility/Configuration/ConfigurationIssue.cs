namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Severity of a <see cref="ConfigurationIssue" />.
/// </summary>
public enum IssueSeverity
{
	/// <summary>Informational, nothing to fix.</summary>
	Info,

	/// <summary>Something is probably wrong but the package keeps working.</summary>
	Warning,

	/// <summary>The configuration is invalid; the package fails open until it is fixed.</summary>
	Error,
}

/// <summary>
///     One diagnostic about the configuration, identified by a stable <see cref="IssueCodes" /> code.
/// </summary>
/// <param name="Code">The <see cref="IssueCodes" /> code.</param>
/// <param name="Severity">How serious the issue is.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="Path">
///     Optional location: a configuration path such as <c>Sites:corporate</c>, or a JSON path in the rules file such as
///     <c>$.Sites.corporate.RootNodeNme</c>.
/// </param>
/// <param name="Suggestion">
///     Optional did-you-mean value, such as the known key closest to a misspelled one (<see cref="IssueCodes.UnknownKey" />).
/// </param>
public sealed record ConfigurationIssue(string Code, IssueSeverity Severity, string Message, string? Path = null, string? Suggestion = null)
{
	/// <summary>
	///     Formats the issue as <c>CODE: message</c>, with the path in parentheses when present and a
	///     <c>Did you mean '...'?</c> sentence when a suggestion is present.
	/// </summary>
	public override string ToString()
	{
		var text = Path is null ? $"{Code}: {Message}" : $"{Code} ({Path}): {Message}";
		return Suggestion is null ? text : $"{text} Did you mean '{Suggestion}'?";
	}
}
