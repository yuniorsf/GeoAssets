using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

internal sealed class CountingLogger<T> : ILogger<T>
{
    public int Warnings { get; private set; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) Warnings++;
    }
}
