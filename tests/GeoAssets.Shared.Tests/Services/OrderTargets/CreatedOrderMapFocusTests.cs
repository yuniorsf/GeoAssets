using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>Pointing the map at a newly created order (XD01-312).</summary>
public sealed class CreatedOrderMapFocusTests
{
    private sealed class RecordingFocus : IMapOrderFocus
    {
        public List<string> Calls { get; } = [];
        public CancellationToken? Token { get; private set; }
        public bool Fits { get; init; } = true;
        public Exception? FitFailure { get; init; }
        public Exception? OutlineFailure { get; init; }

        public Task<bool> FitToExtentAsync(OrderTargetExtent? extent) => throw new InvalidOperationException("not used");

        public Task<bool> FitToOrderAsync(string orderId, CancellationToken ct)
        {
            Calls.Add("fit:" + orderId);
            Token = ct;
            return FitFailure is null ? Task.FromResult(Fits) : Task.FromException<bool>(FitFailure);
        }

        public Task<bool> ShowFenceOutlineAsync(FeatureSelectionSpec? spec) => throw new InvalidOperationException("not used");

        public Task<bool> ShowOrderFenceOutlineAsync(string orderId, CancellationToken ct)
        {
            Calls.Add("outline:" + orderId);
            return OutlineFailure is null ? Task.FromResult(true) : Task.FromException<bool>(OutlineFailure);
        }

        public Task ClearFenceOutlineAsync() => throw new InvalidOperationException("not used");
    }

    private sealed class CountingLogger : ILogger
    {
        public int Warnings { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
        }
    }

    [Fact]
    public async Task Guards()
    {
        var focus = new RecordingFocus();

        await FluentActions.Awaiting(() => CreatedOrderMapFocus.FocusAsync(null!, "o1", NullLogger.Instance, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => CreatedOrderMapFocus.FocusAsync(focus, "o1", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => CreatedOrderMapFocus.FocusAsync(focus, " ", NullLogger.Instance, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        focus.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FitsThenOutlines_ForwardingTheToken_AndReportsWhetherItFitted(bool fits)
    {
        var focus = new RecordingFocus { Fits = fits };
        using var cts = new CancellationTokenSource();

        var fitted = await CreatedOrderMapFocus.FocusAsync(focus, "o1", NullLogger.Instance, cts.Token);

        fitted.Should().Be(fits);
        focus.Calls.Should().Equal("fit:o1", "outline:o1");
        focus.Token.Should().Be(cts.Token);
    }

    [Fact]
    public async Task AFailedFit_IsLogged_AndTheOutlineStillRuns()
    {
        var focus = new RecordingFocus { FitFailure = new HttpRequestException("down") };
        var logger = new CountingLogger();

        var fitted = await CreatedOrderMapFocus.FocusAsync(focus, "o1", logger, CancellationToken.None);

        fitted.Should().BeFalse();
        focus.Calls.Should().Equal("fit:o1", "outline:o1");
        logger.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task AFailedOutline_IsLogged_NotThrown()
    {
        var focus = new RecordingFocus { OutlineFailure = new InvalidOperationException("js") };
        var logger = new CountingLogger();

        var fitted = await CreatedOrderMapFocus.FocusAsync(focus, "o1", logger, CancellationToken.None);

        fitted.Should().BeTrue();
        logger.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var focus = new RecordingFocus { FitFailure = new OperationCanceledException() };

        await FluentActions.Awaiting(() => CreatedOrderMapFocus.FocusAsync(focus, "o1", NullLogger.Instance, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
        focus.Calls.Should().Equal("fit:o1");
    }

    [Fact]
    public async Task CancellationDuringTheOutline_Propagates()
    {
        var focus = new RecordingFocus { OutlineFailure = new OperationCanceledException() };

        await FluentActions.Awaiting(() => CreatedOrderMapFocus.FocusAsync(focus, "o1", NullLogger.Instance, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
        focus.Calls.Should().Equal("fit:o1", "outline:o1");
    }
}
