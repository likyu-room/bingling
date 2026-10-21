// Minimal file logger: %APPDATA%\Bingling\logs\bingling-yyyyMMdd.log, keeps the last 7 days.

using Microsoft.Extensions.Logging;

namespace Filee.App.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    // Shared by every provider: Program's early logger and AppHost's write to the same file.
    private static readonly Lock WriteLock = new();
    private readonly string _directory;

    /// <summary>Start of the daily log file names (StartupFailure writes the same files).</summary>
    internal const string FilePrefix = "bingling-";

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        // filee-*.log: the logs of Filee Plus (1.1.0), which came along with its moved data folder.
        foreach (var old in new[] { FilePrefix + "*.log", "filee-*.log" }.SelectMany(p => Directory.EnumerateFiles(directory, p))
                     .Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)))
        {
            try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (WriteLock)
        {
            try
            {
                File.AppendAllText(Path.Combine(_directory, $"{FilePrefix}{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var shortCategory = category[(category.LastIndexOf('.') + 1)..];
            var line = $"{DateTime.Now:HH:mm:ss.fff} {logLevel.ToString()[..4].ToUpperInvariant()} {shortCategory}: {formatter(state, exception)}";
            if (exception is not null)
                line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}
