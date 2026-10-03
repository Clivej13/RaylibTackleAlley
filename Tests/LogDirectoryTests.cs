using RaylibTackleAlley.Application;
using Xunit;

public sealed class LogDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LogDirectoryTests", Guid.NewGuid().ToString("N"));
    private string AppData => Path.Combine(_root, "appdata");

    public LogDirectoryTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "RaylibTackleAlley.csproj"), "<Project />");
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void RepositoryBuildUsesArtifacts(string configuration)
    {
        string executable = Path.Combine(_root, "bin", configuration, "net9.0");
        Assert.Equal(Path.Combine(_root, "artifacts", "logs"),
            GameDiagnostics.ResolveDirectory(executable, AppData));
        Assert.Equal(Path.Combine(_root, "custom-logs"),
            GameDiagnostics.ResolveDirectory(executable, AppData, "custom-logs"));
    }

    [Theory]
    [InlineData("installed")]
    [InlineData("bin/Release/net9.0/publish")]
    public void InstalledOrPublishedBuildUsesAppData(string location)
    {
        string executable = Path.Combine(_root, location.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(Path.Combine(AppData, "RaylibTackleAlley", "Logs"),
            GameDiagnostics.ResolveDirectory(executable, AppData));
        Assert.Equal(Path.Combine(executable, "custom-logs"),
            GameDiagnostics.ResolveDirectory(executable, AppData, "custom-logs"));
    }

    [Fact]
    public void AbsoluteOverrideIsPreserved()
    {
        string destination = Path.Combine(_root, "explicit");
        Assert.Equal(destination, GameDiagnostics.ResolveDirectory(
            Path.Combine(_root, "bin", "Release", "net9.0"), AppData, destination));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
