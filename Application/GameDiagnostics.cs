using System.Text.Json;
using RaylibGameFramework.Logging;

namespace RaylibTackleAlley.Application;

public sealed class GameLogConfiguration
{
    public string? Directory { get; set; }
    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;
    public int RetainedFiles { get; set; } = 5;
}

public static class GameDiagnostics
{
    // Use the executable location, never the launcher's working directory.
    // Both Debug and Release under the repository's bin tree are development runs.
    public static string ResolveDirectory(string baseDirectory, string localAppData, string? configuredDirectory = null)
    {
        string applicationDirectory = Path.GetFullPath(baseDirectory);
        string? repository = null;
        for (var parent = new DirectoryInfo(applicationDirectory); parent is not null; parent = parent.Parent)
        {
            if (!File.Exists(Path.Combine(parent.FullName, "RaylibTackleAlley.csproj"))) continue;
            string relative = Path.GetRelativePath(parent.FullName, applicationDirectory);
            string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length > 1 && parts[0].Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !parts.Any(part => part.Equals("publish", StringComparison.OrdinalIgnoreCase)))
                repository = parent.FullName;
            break;
        }
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            return Path.GetFullPath(configuredDirectory, repository ?? applicationDirectory);
        return repository is not null
            ? Path.Combine(repository, "artifacts", "logs")
            : Path.Combine(localAppData, "RaylibTackleAlley", "Logs");
    }

    public static RotatingFileLogger Create()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string directory = ResolveDirectory(AppContext.BaseDirectory, localAppData);
        var fallback = new RotatingFileLogger(directory);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "logging.json");
            var config = File.Exists(path)
                ? JsonSerializer.Deserialize<GameLogConfiguration>(File.ReadAllText(path))
                    ?? throw new JsonException("logging.json must contain an object.")
                : new GameLogConfiguration();
            directory = ResolveDirectory(AppContext.BaseDirectory, localAppData, config.Directory);
            var logger = new RotatingFileLogger(directory, new FileLogOptions
            {
                MaxFileBytes = config.MaxFileBytes, RetainedFiles = config.RetainedFiles
            });
            logger.Write("INFO", $"Log directory: {directory}");
            return logger;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            fallback.Write("ERROR", $"Invalid logging configuration; using default directory '{fallback.DirectoryPath}' and 5 MiB / five files.", e);
            return fallback;
        }
    }
}
