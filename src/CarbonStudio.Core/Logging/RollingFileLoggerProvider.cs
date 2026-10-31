using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Logging;

public sealed class RollingFileLoggerOptions
{
    public required string Directory { get; init; }

    /// <summary>A new file is started when appending would exceed this size.</summary>
    public long MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Oldest files beyond this count are deleted.</summary>
    public int RetainedFileCount { get; init; } = 10;

    public string FilePrefix { get; init; } = "carbon";

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// Writes log lines to <c>carbon-yyyyMMdd[.n].log</c> on a background task. Files roll daily and by size;
/// old files are pruned. Logging never throws into the caller.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileLoggerOptions _options;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;

    public RollingFileLoggerProvider(RollingFileLoggerOptions options)
    {
        _options = options;
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(5));
    }

    private void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        string? currentFile = null;
        long currentSize = 0;
        string? currentDay = null;

        await foreach (var line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                var day = _options.TimeProvider.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

                var rolled = false;
                if (currentFile is null || day != currentDay || currentSize + bytes.Length > _options.MaxFileSizeBytes)
                {
                    rolled = true;
                    System.IO.Directory.CreateDirectory(_options.Directory);
                    currentFile = NextFile(day, day == currentDay);
                    currentDay = day;
                    currentSize = new FileInfo(currentFile).Exists ? new FileInfo(currentFile).Length : 0;
                }

                await File.AppendAllTextAsync(currentFile, line + Environment.NewLine).ConfigureAwait(false);
                currentSize += bytes.Length;

                if (rolled)
                {
                    // Prune after the new file exists so the retained count includes it.
                    Prune();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down; the line is dropped.
                System.Diagnostics.Debug.WriteLine($"Carbon log write failed: {ex.Message}");
            }
        }
    }

    /// <summary>Picks the file to append to for <paramref name="day"/>, moving to the next index when rolling by size.</summary>
    private string NextFile(string day, bool forceNewIndex)
    {
        var index = 0;
        string Path(int i) => System.IO.Path.Combine(
            _options.Directory,
            i == 0 ? $"{_options.FilePrefix}-{day}.log" : $"{_options.FilePrefix}-{day}.{i}.log");

        while (File.Exists(Path(index + 1)))
        {
            index++;
        }

        var candidate = Path(index);
        if (forceNewIndex && File.Exists(candidate))
        {
            candidate = Path(index + 1);
        }

        return candidate;
    }

    private void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(_options.Directory)
                .EnumerateFiles($"{_options.FilePrefix}-*.log")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(Math.Max(1, _options.RetainedFileCount));

            foreach (var file in stale)
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Carbon log pruning failed: {ex.Message}");
        }
    }

    private sealed class FileLogger(string category, RollingFileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                _ => "CRT",
            };

            var timestamp = owner._options.TimeProvider.GetLocalNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            var message = formatter(state, exception);
            var line = $"{timestamp} [{level}] {category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            owner.Enqueue(line);
        }
    }
}
