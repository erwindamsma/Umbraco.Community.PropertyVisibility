namespace Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;

/// <summary>
///     Reads the rules file again after a read failed because the file could not be opened (an editor or a sync tool still
///     held it locked when the debounce elapsed). Releasing a lock raises no file event, so without a retry the edit would
///     wait for the next change of the file or of appsettings.
/// </summary>
/// <remarks>
///     One instance per application, shared by <see cref="ConfigFileOptionsSetup" /> (which reports every read) and
///     <see cref="ConfigFileChangeTokenSource" /> (which attaches a scheduler that rebuilds the options when a retry is
///     due). Retries back off: 1, 2 and 5 seconds, then every 30 seconds while the file stays locked. A successful read,
///     a file that is gone, or a failure that time does not fix (access denied, a path that is too long) stops the retries
///     and resets the backoff. The scheduler is called on the thread that built the options, and only arms a timer: the
///     change token never fires from inside the options build.
/// </remarks>
internal sealed class ConfigFileReadRetry
{
	/// <summary>The delay before the first, second, third and every later retry.</summary>
	internal static readonly IReadOnlyList<TimeSpan> Backoff =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(30),
	];

	private readonly object _gate = new();
	private Action<TimeSpan>? _scheduler;
	private int _failures;

	/// <summary>Number of unreadable reads since the last successful read.</summary>
	internal int ConsecutiveFailures
	{
		get
		{
			lock (_gate)
			{
				return _failures;
			}
		}
	}

	/// <summary>
	///     Attaches the scheduler that rebuilds the options after the given delay; replaces an earlier one.
	/// </summary>
	/// <param name="scheduler">Arms a rebuild; must not rebuild synchronously.</param>
	internal void Attach(Action<TimeSpan> scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		lock (_gate)
		{
			_scheduler = scheduler;
		}
	}

	/// <summary>
	///     Detaches the scheduler, when it is still the attached one.
	/// </summary>
	/// <param name="scheduler">The scheduler passed to <see cref="Attach" />.</param>
	internal void Detach(Action<TimeSpan> scheduler)
	{
		lock (_gate)
		{
			if (_scheduler == scheduler)
			{
				_scheduler = null;
			}
		}
	}

	/// <summary>
	///     Reports a read that failed because the file could not be opened, and schedules the next read.
	/// </summary>
	/// <returns>The delay before the next read.</returns>
	internal TimeSpan ReadFailed()
	{
		Action<TimeSpan>? scheduler;
		TimeSpan delay;
		lock (_gate)
		{
			delay = Backoff[Math.Min(_failures, Backoff.Count - 1)];
			_failures++;
			scheduler = _scheduler;
		}

		scheduler?.Invoke(delay);
		return delay;
	}

	/// <summary>
	///     Reports a read that needs no retry (it succeeded, the file is gone or turned off, or it failed for a reason a
	///     retry does not fix, such as denied access): stops the retries and resets the backoff.
	/// </summary>
	internal void ReadSucceeded()
	{
		lock (_gate)
		{
			_failures = 0;
		}
	}
}
