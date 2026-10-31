using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Output;

public sealed record OutputLine(DateTimeOffset Timestamp, string Channel, string Text);

/// <summary>In-memory output channels shown in the Output panel (bounded per channel).</summary>
public interface IOutputService
{
    event EventHandler<OutputLine>? LineWritten;

    void Write(string channel, string text);

    IReadOnlyList<OutputLine> Snapshot(string channel);

    void Clear(string channel);
}

public sealed class OutputService : IOutputService
{
    public const int MaxLinesPerChannel = 5000;

    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<OutputLine>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public OutputService(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public event EventHandler<OutputLine>? LineWritten;

    public void Write(string channel, string text)
    {
        var line = new OutputLine(_time.GetLocalNow(), channel, text);
        lock (_lock)
        {
            if (!_channels.TryGetValue(channel, out var queue))
            {
                queue = new Queue<OutputLine>();
                _channels[channel] = queue;
            }

            queue.Enqueue(line);
            while (queue.Count > MaxLinesPerChannel)
            {
                queue.Dequeue();
            }
        }

        LineWritten?.Invoke(this, line);
    }

    public IReadOnlyList<OutputLine> Snapshot(string channel)
    {
        lock (_lock)
        {
            return _channels.TryGetValue(channel, out var queue) ? [.. queue] : [];
        }
    }

    public void Clear(string channel)
    {
        lock (_lock)
        {
            _channels.Remove(channel);
        }
    }
}

/// <summary>Mirrors Information-and-above log entries into the "Carbon" output channel.</summary>
public sealed class OutputLoggerProvider(IOutputService output) : ILoggerProvider
{
    public const string ChannelName = "Carbon";

    public ILogger CreateLogger(string categoryName) => new OutputLogger(output, categoryName);

    public void Dispose()
    {
    }

    private sealed class OutputLogger(IOutputService output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = category[(category.LastIndexOf('.') + 1)..];
            var text = $"[{logLevel.ToString()[..3].ToUpperInvariant()}] {shortCategory}: {formatter(state, exception)}";
            if (exception is not null)
            {
                text += $" ({exception.GetType().Name}: {exception.Message})";
            }

            output.Write(ChannelName, text);
        }
    }
}
