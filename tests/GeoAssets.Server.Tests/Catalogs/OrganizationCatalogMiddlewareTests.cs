using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Server.Tests.Catalogs;

/// <summary><c>UseOrganizationAssetCatalogs</c> (XD01-373): warms the caller's catalog through the port.</summary>
public sealed class OrganizationCatalogMiddlewareTests
{
    private static readonly Guid Org = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId => organizationId;
    }

    private sealed class FakeFactory(Exception? failure = null) : IOrganizationCatalogFactory
    {
        public List<Guid> Warmed { get; } = [];

        public IAssetProvider GetForOrganization(Guid organizationId) => new TestAssetProvider();

        public ValueTask<IAssetProvider> GetForOrganizationAsync(Guid organizationId, CancellationToken ct)
        {
            Warmed.Add(organizationId);
            return failure is null ? ValueTask.FromResult(GetForOrganization(organizationId)) : throw failure;
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Entries);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static async Task<(HttpResponseMessage Response, bool EndpointReached)> SendAsync(
        FakeFactory? factory, Guid? organizationId, RecordingLoggerProvider? logs = null)
    {
        var reached = false;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (logs is not null) builder.Logging.AddProvider(logs);
        if (factory is not null) builder.Services.AddSingleton<IOrganizationCatalogFactory>(factory);
        if (organizationId is { } id) builder.Services.AddScoped<ITenantContext>(_ => new FixedTenant(id));

        await using var app = builder.Build();
        app.UseOrganizationAssetCatalogs();
        app.Run(context =>
        {
            reached = true;
            return context.Response.WriteAsync("ok");
        });
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        await app.StopAsync();
        return (response, reached);
    }

    [Fact]
    public async Task UseOrganizationAssetCatalogs_WarmsTheCallersOrganization()
    {
        var factory = new FakeFactory();

        var (response, reached) = await SendAsync(factory, Org);

        response.IsSuccessStatusCode.Should().BeTrue();
        reached.Should().BeTrue();
        factory.Warmed.Should().Equal(Org);
    }

    [Fact]
    public async Task UseOrganizationAssetCatalogs_EmptyOrganization_DoesNotWarm()
    {
        var factory = new FakeFactory();

        var (_, reached) = await SendAsync(factory, Guid.Empty);

        reached.Should().BeTrue();
        factory.Warmed.Should().BeEmpty();
    }

    [Fact]
    public async Task UseOrganizationAssetCatalogs_NoTenantContext_DoesNotWarm()
    {
        var factory = new FakeFactory();

        var (_, reached) = await SendAsync(factory, organizationId: null);

        reached.Should().BeTrue();
        factory.Warmed.Should().BeEmpty();
    }

    [Fact]
    public async Task UseOrganizationAssetCatalogs_FactoryThrows_LogsAWarningAndContinues()
    {
        var factory = new FakeFactory(new InvalidOperationException("store down"));
        var logs = new RecordingLoggerProvider();

        var (response, reached) = await SendAsync(factory, Org, logs);

        response.IsSuccessStatusCode.Should().BeTrue();
        reached.Should().BeTrue();
        factory.Warmed.Should().Equal(Org);
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(Org.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UseOrganizationAssetCatalogs_NoFactoryRegistered_IsANoOp()
    {
        var (response, reached) = await SendAsync(factory: null, Org);

        response.IsSuccessStatusCode.Should().BeTrue();
        reached.Should().BeTrue();
    }

    [Fact]
    public void UseOrganizationAssetCatalogs_NullApp_Throws()
    {
        var act = () => OrganizationAssetCatalogHostExtensions.UseOrganizationAssetCatalogs(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
