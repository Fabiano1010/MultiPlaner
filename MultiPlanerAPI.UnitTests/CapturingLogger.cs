using Microsoft.Extensions.Logging;

namespace MultiPlanerAPI.UnitTests;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Errors { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
        {
            Errors.Add(formatter(state, exception));
        }
    }
}
