using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Server.Modules.NeutralOperator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

public sealed class ModuleProblemMappersRegistrationTests
{
    [Fact]
    public void AddServerOrderOccupancyStaging_RegistersItsMapperOnce_EvenWhenCalledTwice()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddServerOrderOccupancyStaging(configuration);
        services.AddServerOrderOccupancyStaging(configuration);

        services.Where(d => d.ServiceType == typeof(IProblemDetailsMapper)).Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(OccupancyProblemMapper) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddServerSharedUseDispatch_RegistersItsMapperOnce_EvenWhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddServerSharedUseDispatch();
        services.AddServerSharedUseDispatch();

        services.Where(d => d.ServiceType == typeof(IProblemDetailsMapper)).Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(SharedUseProblemMapper) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void BothModules_RegisterEachMapperExactlyOnce()
    {
        var services = new ServiceCollection();

        services.AddServerOrderOccupancyStaging(new ConfigurationBuilder().Build());
        services.AddServerSharedUseDispatch();

        services.Where(d => d.ServiceType == typeof(IProblemDetailsMapper)).Select(d => d.ImplementationType)
            .Should().BeEquivalentTo([typeof(OccupancyProblemMapper), typeof(SharedUseProblemMapper)]);
    }
}
