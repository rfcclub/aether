using System.Text;
using Microsoft.Extensions.Logging;

namespace Aether;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly ILogger _logger;

    public FileLoggerProvider(string path)
    {
        _path = path;
        _logger = new FileLogger(this);
    }

    public ILogger CreateLogger(string categoryName) => _logger;

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;

        public FileLogger(FileLoggerProvider provider) => _provider = provider;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var msg = new StringBuilder();
            msg.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            msg.Append(" [").Append(logLevel.ToString()[..4]).Append("] ");
            msg.Append(formatter(state, exception));
            if (exception is not null)
            {
                msg.AppendLine();
                msg.Append(exception);
            }
            msg.AppendLine();

            lock (_provider._lock)
            {
                var dir = Path.GetDirectoryName(_provider._path);
                if (dir is not null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_provider._path, msg.ToString());
            }
        }
    }
}
