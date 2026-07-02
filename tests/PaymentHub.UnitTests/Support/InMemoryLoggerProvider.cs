using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// In-memory <see cref="ILoggerProvider"/> used by slice 9-O1 observability
/// tests to capture log records without depending on Serilog's sinks.
/// Records are stored in arrival order and exposed through
/// <see cref="Records"/>.
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// One captured log record. The shape mirrors the standard
    /// <see cref="LogLevel"/> + structured-state surface without depending
    /// on the framework internals (we copy <c>state</c> as a string for
    /// predictable assertions).
    /// </summary>
    public sealed record Record(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);

    private readonly ConcurrentQueue<Record> _records = new();
    private readonly Func<string, LogLevel, bool>? _filter;

    public InMemoryLoggerProvider(Func<string, LogLevel, bool>? filter = null)
    {
        _filter = filter;
    }

    /// <summary>
    /// All captured records in arrival order. The collection is a snapshot —
    /// subsequent emissions are not reflected on the returned reference.
    /// </summary>
    public IReadOnlyList<Record> Records => _records.ToArray();

    public ILogger CreateLogger(string categoryName)
        => new InMemoryLogger(categoryName, _filter, _records);

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly string _category;
        private readonly Func<string, LogLevel, bool>? _filter;
        private readonly ConcurrentQueue<Record> _sink;

        public InMemoryLogger(string category, Func<string, LogLevel, bool>? filter, ConcurrentQueue<Record> sink)
        {
            _category = category;
            _filter = filter;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => _filter?.Invoke(_category, logLevel) ?? true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            _sink.Enqueue(new Record(_category, logLevel, eventId, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
