namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Default <see cref="IConfigurationInfo" />: an immutable <see cref="ConfigurationState" /> swapped atomically by
///     <see cref="ConfigFileOptionsSetup" />, so readers never see a half-updated load, plus the watch problem that
///     <see cref="ConfigFileChangeTokenSource" /> reports (<see cref="IssueCodes.ConfigFileNotWatched" />).
/// </summary>
public sealed class ConfigurationInfo : IConfigurationInfo
{
	private ConfigurationState _current = ConfigurationState.NotLoaded;
	private ConfigurationIssue? _watchIssue;

	/// <inheritdoc />
	public ConfigurationState Current
	{
		get
		{
			ConfigurationState state = LoadState;
			ConfigurationIssue? watchIssue = Volatile.Read(ref _watchIssue);
			return watchIssue is null ? state : state with { Issues = [.. state.Issues, watchIssue] };
		}
	}

	/// <inheritdoc />
	public ConfigurationSource ActiveSource => LoadState.ActiveSource;

	/// <inheritdoc />
	public string? FilePath => LoadState.FilePath;

	/// <inheritdoc />
	public DateTimeOffset? LoadedAt => LoadState.LoadedAt;

	/// <inheritdoc />
	public string? RulesHash => LoadState.RulesHash;

	/// <inheritdoc />
	public IReadOnlyList<ConfigurationIssue> Issues => Current.Issues;

	/// <summary>
	///     The state of the last load as <see cref="ConfigFileOptionsSetup" /> published it, without the watch problem.
	/// </summary>
	internal ConfigurationState LoadState => Volatile.Read(ref _current);

	/// <summary>
	///     The watch problem currently reported, or <c>null</c> when the rules file is watched (or the file source is off).
	/// </summary>
	internal ConfigurationIssue? WatchIssue => Volatile.Read(ref _watchIssue);

	/// <summary>
	///     Publishes a new load state.
	/// </summary>
	/// <param name="state">The state of the load that just completed.</param>
	internal void Update(ConfigurationState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		Volatile.Write(ref _current, state);
	}

	/// <summary>
	///     Sets or clears the watch problem (<see cref="IssueCodes.ConfigFileNotWatched" />).
	/// </summary>
	/// <param name="issue">The problem, or <c>null</c> once the file is watched again.</param>
	internal void SetWatchIssue(ConfigurationIssue? issue) => Volatile.Write(ref _watchIssue, issue);
}
