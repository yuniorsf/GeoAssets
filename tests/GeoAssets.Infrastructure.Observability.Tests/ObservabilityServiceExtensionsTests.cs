using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class ObservabilityServiceExtensionsTests
{
    private static TracerProvider BuildTracerProvider(IDictionary<string, string?>? config = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
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

    [Fact]
    public void AddGeoAssetsObservability_ConfiguresAlwaysOnSampler()
    {
        using var tracerProvider = BuildTracerProvider();

        var sampler = GetSampler(tracerProvider);

        sampler.Should().BeOfType<AlwaysOnSampler>();
    }
}
