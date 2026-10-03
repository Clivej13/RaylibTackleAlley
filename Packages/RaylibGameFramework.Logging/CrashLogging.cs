namespace RaylibGameFramework.Logging;

public sealed class CrashLogging : IDisposable
{
    private readonly RotatingFileLogger _logger;
    public CrashLogging(RotatingFileLogger logger)
    {
        _logger = logger;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs args) =>
        _logger.Write("FATAL", $"Unhandled exception; terminating={args.IsTerminating}",
            args.ExceptionObject as Exception);

    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args) =>
        _logger.Write("ERROR", "Unobserved task exception", args.Exception);

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }
}
