using CarbonStudio.Core.Logging;
using CarbonStudio.Core.Output;
using Microsoft.Extensions.Logging;

namespace CarbonStudio.Core.Tests;

public class RollingFileLoggerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string LogDir => _dir.Combine("logs");

    private static string[] ReadAllLines(string dir) =>
        Directory.GetFiles(dir, "carbon-*.log").OrderBy(f => f, StringComparer.Ordinal).SelectMany(File.ReadAllLines).ToArray();

    [Fact]
    public void WritesFormattedLines_IncludingExceptions_AndFlushesOnDispose()
    {
        using (var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions { Directory = LogDir }))
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.LogInformation("hello {Name}", "world");
            logger.LogError(new InvalidOperationException("boom"), "failed");
        }

        var text = string.Join("\n", ReadAllLines(LogDir));
        Assert.Contains("[INF] Test.Category: hello world", text);
        Assert.Contains("[ERR] Test.Category: failed", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void RollsToNewFile_WhenSizeLimitIsReached()
    {
        using (var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions { Directory = LogDir, MaxFileSizeBytes = 400 }))
        {
            var logger = provider.CreateLogger("Size");
            for (var i = 0; i < 20; i++)
            {
                logger.LogInformation("message number {Index} with some padding text", i);
            }
        }

        var files = Directory.GetFiles(LogDir, "carbon-*.log");
        Assert.True(files.Length > 1, $"expected several files, found {files.Length}");
        Assert.Equal(20, ReadAllLines(LogDir).Count(l => l.Contains("message number", StringComparison.Ordinal)));
    }

    [Fact]
    public void PrunesOldFiles_BeyondRetentionCount()
    {
        Directory.CreateDirectory(LogDir);
        for (var day = 1; day <= 6; day++)
        {
            File.WriteAllText(Path.Combine(LogDir, $"carbon-2020010{day}.log"), "old");
        }

        using (var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions { Directory = LogDir, RetainedFileCount = 3 }))
        {
            provider.CreateLogger("Prune").LogInformation("trigger");
        }

        var remaining = Directory.GetFiles(LogDir, "carbon-*.log").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(3, remaining.Length);
        Assert.DoesNotContain("carbon-20200101.log", remaining);
        Assert.Single(remaining, n => !n!.StartsWith("carbon-2020", StringComparison.Ordinal)); // today's file survives pruning
    }

    [Fact]
    public void UnwritableDirectory_DoesNotThrowIntoCaller()
    {
        var blocker = _dir.WriteFile("blocker", "this is a file, not a directory");
        using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions { Directory = Path.Combine(blocker, "logs") });
        provider.CreateLogger("Fail").LogInformation("should be dropped silently");
    }
}

public class OutputServiceTests
{
    [Fact]
    public void Write_AppendsToChannel_AndRaisesEvent()
    {
        var output = new OutputService();
        var seen = new List<string>();
        output.LineWritten += (_, line) => seen.Add(line.Text);

        output.Write("Carbon", "one");
        output.Write("Carbon", "two");
        output.Write("Build", "other");

        Assert.Equal(["one", "two"], output.Snapshot("carbon").Select(l => l.Text).ToArray());
        Assert.Equal(["one", "two", "other"], seen.ToArray());
    }

    [Fact]
    public void Channel_IsBounded()
    {
        var output = new OutputService();
        for (var i = 0; i < OutputService.MaxLinesPerChannel + 10; i++)
        {
            output.Write("c", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var lines = output.Snapshot("c");
        Assert.Equal(OutputService.MaxLinesPerChannel, lines.Count);
        Assert.Equal("10", lines[0].Text);
    }

    [Fact]
    public void Clear_RemovesChannelContents()
    {
        var output = new OutputService();
        output.Write("c", "x");
        output.Clear("c");
        Assert.Empty(output.Snapshot("c"));
    }

    [Fact]
    public void LoggerProvider_MirrorsInformationAndAbove_Only()
    {
        var output = new OutputService();
        using var provider = new OutputLoggerProvider(output);
        var logger = provider.CreateLogger("CarbonStudio.Core.Workspace.WorkspaceService");

        logger.LogDebug("hidden");
        logger.LogInformation("shown");
        logger.LogWarning(new IOException("disk"), "careful");

        var lines = output.Snapshot(OutputLoggerProvider.ChannelName).Select(l => l.Text).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal("[INF] WorkspaceService: shown", lines[0]);
        Assert.Contains("IOException: disk", lines[1]);
    }
}
