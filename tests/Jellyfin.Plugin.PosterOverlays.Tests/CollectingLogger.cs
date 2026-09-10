using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was told, so a test can assert on the message a
/// user would actually read rather than on the code path that produced it.
/// </summary>
internal sealed class CollectingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <summary>
    /// Always on. A test that ran against a logger with warnings switched off would pass by
    /// recording nothing, which is the one outcome it must not be able to reach silently.
    /// </summary>
    /// <param name="logLevel">Ignored.</param>
    /// <returns>Always true.</returns>
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
