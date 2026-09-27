using Microsoft.Extensions.Logging;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     Logger that records every entry, so tests can count how often something was logged. Thread-safe: file watchers,
///     timers and background runs log from other threads.
/// </summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class ListLogger<T> : ILogger<T>
{
	private readonly object _gate = new();
	private readonly List<LogEntry> _entries = [];

	/// <summary>A snapshot of every entry logged so far, in order.</summary>
	public IReadOnlyList<LogEntry> Entries
	{
		get
		{
			lock (_gate)
			{
				return _entries.ToList();
			}
		}
	}

	/// <summary>Entries at the given level.</summary>
	public IReadOnlyList<LogEntry> At(LogLevel level) => Entries.Where(entry => entry.Level == level).ToList();

	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
		=> null;

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel) => true;

	/// <inheritdoc />
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		var entry = new LogEntry(logLevel, formatter(state, exception), exception);
		lock (_gate)
		{
			_entries.Add(entry);
		}
	}
}

/// <summary>
///     One recorded log entry.
/// </summary>
/// <param name="Level">The level.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception, when any.</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
