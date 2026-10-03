using System.Text;
using RaylibGameFramework.Logging;
using Xunit;

public sealed class FileLoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TackleAlleyLoggingTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RotationBoundsDiskUsageAndKeepsNewestEventsAcrossSessions()
    {
        for (int session = 0; session < 2; session++)
        {
            var logger = new RotatingFileLogger(_directory, new() { MaxFileBytes = 1024, RetainedFiles = 3 });
            for (int i = 0; i < 20; i++) logger.Write("INFO", $"session={session} entry={i} " + new string('x', 300));
        }
        string[] files = Directory.GetFiles(_directory);
        Assert.Equal(3, files.Length);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 1024));
        Assert.Contains("session=1 entry=19", File.ReadAllText(Path.Combine(_directory, "current.log")));
        Assert.DoesNotContain("session=0", string.Join("", files.Select(File.ReadAllText)));
    }

    [Fact]
    public void OversizedUnicodeEntryIsBoundedAndValidUtf8()
    {
        var logger = new RotatingFileLogger(_directory, new() { MaxFileBytes = 1024, RetainedFiles = 1 });
        logger.Write("ERROR", string.Concat(Enumerable.Repeat("🏈", 1000)));
        byte[] bytes = File.ReadAllBytes(logger.FilePath);
        Assert.InRange(bytes.Length, 1, 1024);
        Assert.Contains("[entry truncated]", new UTF8Encoding(false, true).GetString(bytes));
        logger.Write("INFO", "Next entry");
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Contains("Next entry", File.ReadAllText(logger.FilePath));
    }

    [Fact]
    public void ConcurrentWritersProduceCompleteEntries()
    {
        var logger = new RotatingFileLogger(_directory);
        Parallel.For(0, 100, i => logger.Write("INFO", $"Event {i}"));
        string[] lines = File.ReadAllLines(logger.FilePath);
        Assert.Equal(100, lines.Length);
        for (int i = 0; i < 100; i++) Assert.Single(lines, line => line.EndsWith($"Event {i}"));
    }

    [Fact]
    public void FatalWritesExceptionStackImmediatelyWithoutDispose()
    {
        var logger = new RotatingFileLogger(_directory);
        try { throw new InvalidOperationException("Regression crash"); }
        catch (Exception exception) { logger.Write("FATAL", "Failed", exception); }
        string content = File.ReadAllText(logger.FilePath);
        Assert.Contains("[FATAL]", content);
        Assert.Contains("InvalidOperationException: Regression crash", content);
        Assert.Contains(nameof(FatalWritesExceptionStackImmediatelyWithoutDispose), content);
    }

    [Fact]
    public void ReducedLimitsPruneOlderOversizedFiles()
    {
        var logger = new RotatingFileLogger(_directory, new() { MaxFileBytes = 4096, RetainedFiles = 5 });
        for (int i = 0; i < 10; i++) logger.Write("INFO", new string('x', 3000));
        new RotatingFileLogger(_directory, new() { MaxFileBytes = 1024, RetainedFiles = 2 }).Write("INFO", "New limits");
        Assert.InRange(Directory.GetFiles(_directory).Length, 1, 2);
        Assert.All(Directory.GetFiles(_directory), path => Assert.True(new FileInfo(path).Length <= 1024));
    }

    [Fact]
    public void UnwritableDestinationDoesNotThrow()
    {
        Directory.CreateDirectory(_directory);
        string blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var logger = new RotatingFileLogger(blocked);
        logger.Write("FATAL", "Must not crash");
        logger.Write("INFO", "Must still not crash");
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1024, 0)]
    [InlineData(1024, 101)]
    public void InvalidLimitsFailClearly(long size, int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotatingFileLogger(_directory, new() { MaxFileBytes = size, RetainedFiles = count }));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
