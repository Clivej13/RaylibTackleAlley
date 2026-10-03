using System.Text;

namespace RaylibGameFramework.Logging;

public sealed class FileLogOptions
{
    public long MaxFileBytes { get; set; } = 5 * 1024 * 1024;
    // Includes the active file.
    public int RetainedFiles { get; set; } = 5;

    public void Validate()
    {
        if (MaxFileBytes < 1024 || MaxFileBytes > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxFileBytes), "Use 1024 through 1073741824 bytes.");
        if (RetainedFiles < 1 || RetainedFiles > 100)
            throw new ArgumentOutOfRangeException(nameof(RetainedFiles), "Use 1 through 100 files.");
    }
}

// One writer instance per directory. File handles are closed after each entry so
// previously written events survive process termination without a dispose call.
public sealed class RotatingFileLogger
{
    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly int _retained;
    private bool _reportedFailure;
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "current.log");

    public RotatingFileLogger(string directory, FileLogOptions? options = null)
    {
        options ??= new();
        options.Validate();
        DirectoryPath = Path.GetFullPath(directory);
        _maxBytes = options.MaxFileBytes;
        _retained = options.RetainedFiles;
    }

    public void Write(string level, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string entry = $"{DateTimeOffset.UtcNow:O} [{level}] [pid:{Environment.ProcessId} thread:{Environment.CurrentManagedThreadId}] {message}";
                if (exception is not null) entry += Environment.NewLine + exception;
                byte[] bytes = Encoding.UTF8.GetBytes(entry + "\n");
                if (bytes.LongLength > _maxBytes)
                {
                    const string suffix = "\n[entry truncated]\n";
                    byte[] tail = Encoding.UTF8.GetBytes(suffix);
                    bytes = new byte[(int)_maxBytes];
                    Encoding.UTF8.GetEncoder().Convert(entry.AsSpan(), bytes.AsSpan(0, bytes.Length - tail.Length),
                        true, out _, out int used, out _);
                    tail.CopyTo(bytes, used);
                    Array.Resize(ref bytes, used + tail.Length);
                }
                Prune();
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + bytes.Length > _maxBytes)
                    Rotate();
                using var file = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                file.Write(bytes);
                file.Flush(flushToDisk: level == "FATAL");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Logging must not become a second game failure.
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    try { Console.Error.WriteLine($"File logging failed: {error.Message}"); } catch (IOException) { }
                }
            }
        }
    }

    private string Archive(int index) => Path.Combine(DirectoryPath, $"archive-{index}.log");

    private void Prune()
    {
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "archive-*.log"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(name.AsSpan("archive-".Length), out int index) &&
                (index >= _retained || new FileInfo(path).Length > _maxBytes))
                File.Delete(path);
        }
    }

    private void Rotate()
    {
        if (_retained == 1) { File.Delete(FilePath); return; }
        File.Delete(Archive(_retained - 1));
        for (int i = _retained - 2; i >= 1; i--)
            if (File.Exists(Archive(i))) File.Move(Archive(i), Archive(i + 1), true);
        // A file from an older, larger size setting must not violate the new cap.
        if (new FileInfo(FilePath).Length > _maxBytes) File.Delete(FilePath);
        else File.Move(FilePath, Archive(1), true);
    }
}
