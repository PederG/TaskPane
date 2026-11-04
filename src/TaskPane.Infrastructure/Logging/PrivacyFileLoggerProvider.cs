using Microsoft.Extensions.Logging;
using TaskPane.Core.Logging;
using TaskPane.Infrastructure.Paths;

namespace TaskPane.Infrastructure.Logging;

/// <summary>
/// Simple rolling file logger that redacts likely task content before writing.
/// </summary>
public sealed class PrivacyFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly object _gate = new();
    private bool _disposed;

    public PrivacyFileLoggerProvider(string? dataRoot = null)
    {
        _logDirectory = AppPaths.GetLogDirectory(dataRoot);
    }

    public ILogger CreateLogger(string categoryName) => new PrivacyFileLogger(categoryName, this);

    public void Dispose() => _disposed = true;

    internal void Write(string category, LogLevel level, string message)
    {
        if (_disposed)
        {
            return;
        }

        var safe = LogPrivacy.Sanitize(message);
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {category}: {safe}{Environment.NewLine}";
        var path = Path.Combine(_logDirectory, $"taskpane-{DateTime.UtcNow:yyyyMMdd}.log");

        lock (_gate)
        {
            File.AppendAllText(path, line);
        }
    }

    private sealed class PrivacyFileLogger : ILogger
    {
        private readonly string _category;
        private readonly PrivacyFileLoggerProvider _provider;

        public PrivacyFileLogger(string category, PrivacyFileLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} | {exception.GetType().Name}: {exception.Message}";
            }

            _provider.Write(_category, logLevel, message);
        }
    }
}
