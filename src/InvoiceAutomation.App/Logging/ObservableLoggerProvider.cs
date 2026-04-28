using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.App.Logging;

public sealed class ObservableLoggerProvider : ILoggerProvider
{
    private readonly ObservableLogSink _sink;

    public ObservableLoggerProvider(ObservableLogSink sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new ObservableLogger(categoryName, _sink);

    public void Dispose() { }

    private sealed class ObservableLogger : ILogger
    {
        private readonly string _category;
        private readonly ObservableLogSink _sink;

        public ObservableLogger(string category, ObservableLogSink sink)
        {
            _category = category;
            _sink = sink;
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
                return;
            var msg = formatter(state, exception);
            if (exception is not null)
                msg += " — " + exception.Message;
            _sink.Push($"[{logLevel}] {_category}: {msg}");
        }
    }
}
