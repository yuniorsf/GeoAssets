using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class ObservabilityServiceExtensionsTests
{
    private static TracerProvider BuildTracerProvider(IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>(StringComparer.Ordinal))
            .Build();

        var services = new ServiceCollection();
        services.AddGeoAssetsObservability(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<TracerProvider>();
    }

    // OpenTelemetry doesn't expose the configured Sampler on the public
    // TracerProvider API, so the DI-wired pipeline is inspected via the SDK's
    // private auto-property backing field. Brittle across SDK versions, but
    // this is the only way to assert the sampler without duplicating
    // AddGeoAssetsObservability's wiring logic in the test itself.
    private static Sampler GetSampler(TracerProvider tracerProvider)
    {
        var field = tracerProvider.GetType()
            .GetField("<Sampler>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate the Sampler backing field on TracerProviderSdk via reflection.");

        return (Sampler)field.GetValue(tracerProvider)!;
    }

    private static ServiceProvider BuildServiceProvider(IDictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddGeoAssetsObservability(configuration);
        return services.BuildServiceProvider();
    }

    // Neither SDK provider exposes its exporters publicly: TracerProviderSdk keeps them in its internal
    // `Processor` (null when no processor/exporter was added), MeterProviderSdk in its internal `Reader`.
    private static object? GetInternalProperty(object provider, string name) =>
        (provider.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not locate {provider.GetType().Name}.{name} via reflection."))
        .GetValue(provider);

    private static Dictionary<string, string?> OtlpConfig(string? exportEnabled) => new(StringComparer.Ordinal)
    {
        ["Observability:Otlp:Endpoint"] = "http://localhost:4317",
        ["Observability:ExportEnabled"] = exportEnabled,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void AddGeoAssetsObservability_WithEndpointButExportNotEnabled_RegistersNoExporter(string? exportEnabled)
    {
        using var provider = BuildServiceProvider(OtlpConfig(exportEnabled));

        GetInternalProperty(provider.GetRequiredService<TracerProvider>(), "Processor").Should().BeNull();
        GetInternalProperty(provider.GetRequiredService<MeterProvider>(), "Reader").Should().BeNull();
    }

    [Fact]
    public void AddGeoAssetsObservability_WithEndpointAndExportEnabled_RegistersExporters()
    {
        using var provider = BuildServiceProvider(OtlpConfig("true"));

        GetInternalProperty(provider.GetRequiredService<TracerProvider>(), "Processor").Should().NotBeNull();
        GetInternalProperty(provider.GetRequiredService<MeterProvider>(), "Reader").Should().NotBeNull();
    }

    [Fact]
    public void AddGeoAssetsObservability_ExportEnabledWithoutEndpoint_RegistersNoExporter()
    {
        using var provider = BuildServiceProvider(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Observability:ExportEnabled"] = "true",
        });

        GetInternalProperty(provider.GetRequiredService<TracerProvider>(), "Processor").Should().BeNull();
        GetInternalProperty(provider.GetRequiredService<MeterProvider>(), "Reader").Should().BeNull();
    }

    [Fact]
    public void AddGeoAssetsObservability_ConfiguresAlwaysOnSampler()
    {
        using var tracerProvider = BuildTracerProvider();

        var sampler = GetSampler(tracerProvider);

        sampler.Should().BeOfType<AlwaysOnSampler>();
    }
}
