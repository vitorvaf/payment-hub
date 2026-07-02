using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using PaymentHub.Application.Observability;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// In-memory <see cref="MeterListener"/> that captures every measurement
/// produced through <see cref="PaymentHubMetrics.Meter"/>. Slice 9-O1 tests
/// use it to assert counters/histograms fire without depending on an
/// OpenTelemetry exporter.
///
/// <para>
/// Construction registers a listener on the singleton <c>PaymentHub.Meter</c>.
/// The listener is process-wide; tests that share a fixture should call
/// <see cref="Reset"/> between cases to avoid cross-contamination.
/// </para>
/// </summary>
public sealed class InMemoryMetricsCollector : IDisposable
{
    /// <summary>
    /// One captured measurement.
    /// </summary>
    public sealed record Measurement(
        string InstrumentName,
        object? Value,
        IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener;
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public InMemoryMetricsCollector()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PaymentHubMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.SetMeasurementEventCallback<double>(OnDouble);
        _listener.Start();
    }

    /// <summary>
    /// All captured measurements in arrival order. The collection is a
    /// snapshot — subsequent emissions are not reflected on the returned
    /// reference.
    /// </summary>
    public IReadOnlyList<Measurement> Measurements => _measurements.ToArray();

    /// <summary>
    /// Returns every measurement whose instrument name equals the supplied
    /// value. Convenience for tests that only care about one counter or
    /// histogram.
    /// </summary>
    public IReadOnlyList<Measurement> For(string instrumentName)
        => Measurements.Where(m => m.InstrumentName == instrumentName).ToArray();

    /// <summary>
    /// Clears the captured log. Tests that share a fixture call this between
    /// cases; the listener itself stays alive.
    /// </summary>
    public void Reset()
    {
        while (_measurements.TryDequeue(out _)) { }
    }

    public void Dispose() => _listener.Dispose();

    private void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        => _measurements.Enqueue(new Measurement(instrument.Name, value, ToDict(tags)));

    private void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        => _measurements.Enqueue(new Measurement(instrument.Name, value, ToDict(tags)));

    private static IReadOnlyDictionary<string, object?> ToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
        foreach (var pair in tags) dict[pair.Key] = pair.Value;
        return dict;
    }
}
