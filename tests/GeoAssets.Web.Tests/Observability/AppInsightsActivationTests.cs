using FluentAssertions;
using GeoAssets.Web.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Web.Tests.Observability;

/// <summary>
/// XD01-451: the browser's Application Insights SDK is initialized only when <c>Observability:ExportEnabled</c> is
/// <c>true</c> and a connection string is configured — off by default, like the hosts' OTLP export.
/// </summary>
public sealed class AppInsightsActivationTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://ingest.example.test/";

    private static IConfiguration Configuration(string? exportEnabled, string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [AppInsightsActivation.ExportEnabledKey] = exportEnabled,
                [AppInsightsActivation.ConnectionStringKey] = connectionString,
            })
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("not-a-bool")]
    public void WithAConnectionString_ButExportNotEnabled_StaysOff(string? exportEnabled)
    {
        AppInsightsActivation.ConnectionStringToInitialize(Configuration(exportEnabled, ConnectionString)).Should().BeNull();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void WithExportEnabled_AndAConnectionString_InitializesWithIt(string exportEnabled)
    {
        AppInsightsActivation.ConnectionStringToInitialize(Configuration(exportEnabled, ConnectionString)).Should().Be(ConnectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithExportEnabled_ButNoConnectionString_StaysOff(string? connectionString)
    {
        AppInsightsActivation.ConnectionStringToInitialize(Configuration("true", connectionString)).Should().BeNull();
    }

    [Fact]
    public void TheShippedWebSettings_KeepExportOff()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        var settings = Path.Combine(directory!.FullName, "apps", "GeoAssets.Web", "wwwroot", "appsettings.json");

        var configuration = new ConfigurationBuilder().AddJsonFile(settings).Build();

        configuration[AppInsightsActivation.ExportEnabledKey].Should().Be("False");
    }
}
