using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.NeutralOperator.Rest.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>XD01-388: the NeutralOperator vertical's REST registrations, and that the chassis' own register none of its ports.</summary>
public sealed class NeutralOperatorRestRegistrationTests
{
    private static readonly Type[] VerticalPorts =
    [
        typeof(IOrderOccupancyStagingService), typeof(ISharedUseDispatchService), typeof(ISharedUseDispatchPlanReader), typeof(IWorkflowProblemMapper),
    ];

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddNeutralOperatorWorkflowRest_RegistersTheThreePorts_OnTheRestClients()
    {
        using var provider = Build(s => s.AddNeutralOperatorWorkflowRest("http://test/api/workflow"));

        provider.GetRequiredService<IOrderOccupancyStagingService>().Should().BeOfType<RestOrderOccupancyStagingService>();
        provider.GetRequiredService<ISharedUseDispatchService>().Should().BeOfType<RestSharedUseDispatchClient>();
        provider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeSameAs(provider.GetRequiredService<ISharedUseDispatchService>());
        provider.GetServices<IWorkflowProblemMapper>().Should().ContainSingle().Which.Should().BeOfType<NeutralOperatorProblemMapper>();
    }

    [Fact]
    public void AddDescriptorRoutedNeutralOperatorRest_RegistersTheThreePorts_OnTheRoutedClients()
    {
        using var provider = Build(s =>
        {
            s.AddDescriptorRoutedWorkflowRest("http://gw/api/workflow");
            s.AddDescriptorRoutedNeutralOperatorRest();
        });

        provider.GetRequiredService<IOrderOccupancyStagingService>().Should().BeOfType<DescriptorRoutedOrderOccupancyStagingService>();
        provider.GetRequiredService<ISharedUseDispatchService>().Should().BeOfType<DescriptorRoutedSharedUseDispatchClient>();
        provider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeSameAs(provider.GetRequiredService<ISharedUseDispatchService>());
        provider.GetServices<IWorkflowProblemMapper>().Should().ContainSingle().Which.Should().BeOfType<NeutralOperatorProblemMapper>();
    }

    [Fact]
    public void TheMapper_IsRegisteredOnce_WhateverIsCalledTwiceOrTogether()
    {
        var services = new ServiceCollection();

        services.AddNeutralOperatorWorkflowRest("http://test/api/workflow");
        services.AddNeutralOperatorWorkflowRest("http://test/api/workflow");
        services.AddDescriptorRoutedNeutralOperatorRest();
        services.AddDescriptorRoutedNeutralOperatorRest();

        services.Count(d => d.ServiceType == typeof(IWorkflowProblemMapper)).Should().Be(1);
    }

    [Fact]
    public void TheMapper_IsRegisteredOnce_ForTheRoutedRegistrationCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddDescriptorRoutedNeutralOperatorRest();
        services.AddDescriptorRoutedNeutralOperatorRest();

        services.Count(d => d.ServiceType == typeof(IWorkflowProblemMapper)).Should().Be(1);
    }

    [Fact]
    public void TheRegistrations_GuardTheirArguments()
    {
        var nullServices = () => NeutralOperatorRestServiceExtensions.AddNeutralOperatorWorkflowRest(null!, "http://test/");
        var nullRouted = () => NeutralOperatorRestServiceExtensions.AddDescriptorRoutedNeutralOperatorRest(null!);
        var nullBase = () => new ServiceCollection().AddNeutralOperatorWorkflowRest(null!);
        var blankBase = () => new ServiceCollection().AddNeutralOperatorWorkflowRest("  ");

        nullServices.Should().Throw<ArgumentNullException>();
        nullRouted.Should().Throw<ArgumentNullException>();
        nullBase.Should().Throw<ArgumentNullException>();
        blankBase.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheChassisRegistrations_RegisterNoNeutralOperatorPort()
    {
        var plain = new ServiceCollection().AddWorkflowRest("http://test/api/workflow");
        var routed = new ServiceCollection().AddDescriptorRoutedWorkflowRest("http://gw/api/workflow");

        foreach (var port in VerticalPorts)
        {
            plain.Should().NotContain(d => d.ServiceType == port, "AddWorkflowRest names no vertical (XD01-388): {0}", port.Name);
            routed.Should().NotContain(d => d.ServiceType == port, "AddDescriptorRoutedWorkflowRest names no vertical (XD01-388): {0}", port.Name);
        }
    }
}
