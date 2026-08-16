using System.Diagnostics;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class TelemetryEnrichmentMiddlewareTests
{
    /// <summary>Captures the scope dictionary passed to <see cref="ILogger.BeginScope{TState}"/>.</summary>
    private sealed class CapturingLogger : ILogger<TelemetryEnrichmentMiddleware>
    {
        public IReadOnlyDictionary<string, object?>? LastScope { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            LastScope = (IReadOnlyDictionary<string, object?>)state;
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static (TelemetryEnrichmentMiddleware Middleware, CapturingLogger Logger, bool[] NextCalled) BuildMiddleware()
    {
        var logger = new CapturingLogger();
        var nextCalled = new bool[1];
        RequestDelegate next = _ => { nextCalled[0] = true; return Task.CompletedTask; };
        return (new TelemetryEnrichmentMiddleware(next, logger), logger, nextCalled);
    }

    private static DefaultHttpContext AnonymousContext(string path = "/features", string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        var (middleware, _, nextCalled) = BuildMiddleware();

        await middleware.InvokeAsync(AnonymousContext());

        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AlwaysIncludesRequestPathAndMethod()
    {
        var (middleware, logger, _) = BuildMiddleware();

        await middleware.InvokeAsync(AnonymousContext("/features/bulk", "POST"));

        logger.LastScope.Should().NotBeNull();
        logger.LastScope!["RequestPath"].Should().Be("/features/bulk");
        logger.LastScope!["RequestMethod"].Should().Be("POST");
    }

    [Fact]
    public async Task InvokeAsync_NoActiveActivity_OmitsTraceFields()
    {
        var (middleware, logger, _) = BuildMiddleware();
        Activity.Current.Should().BeNull("a stray Activity from another test would invalidate this case");

        await middleware.InvokeAsync(AnonymousContext());

        logger.LastScope!.Should().NotContainKeys("TraceId", "SpanId", "TraceFlags");
    }

    [Fact]
    public async Task InvokeAsync_WithActiveActivity_IncludesTraceFields()
    {
        var (middleware, logger, _) = BuildMiddleware();
        using var activity = new Activity("test-request").Start();

        await middleware.InvokeAsync(AnonymousContext());

        logger.LastScope!["TraceId"].Should().Be(activity.TraceId.ToString());
        logger.LastScope!["SpanId"].Should().Be(activity.SpanId.ToString());
        logger.LastScope!["TraceFlags"].Should().Be(activity.ActivityTraceFlags.ToString());
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_OmitsUserId()
    {
        var (middleware, logger, _) = BuildMiddleware();

        await middleware.InvokeAsync(AnonymousContext());

        logger.LastScope!.Should().NotContainKey("UserId");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithOidClaim_UsesOidAsUserId()
    {
        var (middleware, logger, _) = BuildMiddleware();
        var ctx = AnonymousContext();
        ctx.User = AuthenticatedPrincipal([new Claim("oid", "user-oid"), new Claim("sub", "user-sub")]);

        await middleware.InvokeAsync(ctx);

        logger.LastScope!["UserId"].Should().Be("user-oid");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithSubClaimOnly_FallsBackToSub()
    {
        var (middleware, logger, _) = BuildMiddleware();
        var ctx = AnonymousContext();
        ctx.User = AuthenticatedPrincipal([new Claim("sub", "user-sub")]);

        await middleware.InvokeAsync(ctx);

        logger.LastScope!["UserId"].Should().Be("user-sub");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithNoOidOrSubClaim_FallsBackToIdentityName()
    {
        var (middleware, logger, _) = BuildMiddleware();
        var ctx = AnonymousContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user-name")], authenticationType: "Test");
        ctx.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(ctx);

        logger.LastScope!["UserId"].Should().Be("user-name");
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}
