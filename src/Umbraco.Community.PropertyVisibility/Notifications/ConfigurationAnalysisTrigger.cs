namespace Umbraco.Community.PropertyVisibility.Notifications;

/// <summary>
///     Asks <see cref="ConfigurationAnalysisLogger" /> for a new run when the root nodes change after startup (a root is
///     created, renamed, moved, trashed or deleted), so a site that stops matching is logged when it happens and not only at
///     the next configuration change.
/// </summary>
/// <remarks>
///     A singleton with no dependencies, so the content notification handlers that use it
///     (<see cref="RootNodeNameCacheInvalidator" />) never depend on the package's options: a content save must not fail
///     because of the package's configuration. The logger subscribes when it is created; requests made before that are
///     dropped, because its startup run covers them. The logger merges requests and runs in the background.
/// </remarks>
public sealed class ConfigurationAnalysisTrigger
{
	private readonly object _gate = new();
	private Action? _listener;
	private HashSet<Guid>? _analyzedRoots;

	/// <summary>
	///     Asks for a new analysis run.
	/// </summary>
	public void Request()
	{
		Action? listener;
		lock (_gate)
		{
			listener = _listener;
		}

		listener?.Invoke();
	}

	/// <summary>
	///     Asks for a new analysis run when the root node keys differ from the ones the last run saw. Nothing happens before
	///     the first run.
	/// </summary>
	/// <param name="rootKeys">The current root node keys.</param>
	public void RequestIfRootsChanged(IEnumerable<Guid> rootKeys)
	{
		ArgumentNullException.ThrowIfNull(rootKeys);

		lock (_gate)
		{
			if (_analyzedRoots is null || _analyzedRoots.SetEquals(rootKeys))
			{
				return;
			}
		}

		Request();
	}

	/// <summary>
	///     Records the root node keys an analysis run saw.
	/// </summary>
	/// <param name="rootKeys">The root node keys of the run.</param>
	internal void Analyzed(IEnumerable<Guid> rootKeys)
	{
		var roots = new HashSet<Guid>(rootKeys);
		lock (_gate)
		{
			_analyzedRoots = roots;
		}
	}

	/// <summary>
	///     Sets the listener that runs the analysis; replaces an earlier one.
	/// </summary>
	/// <param name="listener">Queues a run; must return quickly.</param>
	/// <returns>Removes the listener again.</returns>
	internal IDisposable Subscribe(Action listener)
	{
		ArgumentNullException.ThrowIfNull(listener);
		lock (_gate)
		{
			_listener = listener;
		}

		return new Subscription(this, listener);
	}

	private void Unsubscribe(Action listener)
	{
		lock (_gate)
		{
			if (_listener == listener)
			{
				_listener = null;
			}
		}
	}

	private sealed class Subscription(ConfigurationAnalysisTrigger owner, Action listener) : IDisposable
	{
		public void Dispose() => owner.Unsubscribe(listener);
	}
}
