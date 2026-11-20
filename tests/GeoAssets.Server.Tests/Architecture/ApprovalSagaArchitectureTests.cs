using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Modules.Occupancy;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Where the approval saga's halves live (XD01-383, ADR-001): the kernel's consumer of the asset mutation batches is a kernel type —
/// vertical-agnostic, registered whatever <c>GeoAssets:Modules</c> says — and names no vertical type; the saga's own consumers,
/// service and sweeper are the NeutralOperator module's, present only with the module; and the module's routes add the retry
/// route and no other saga route, none of which the checked-in (cut-over, XD01-384) snapshot holds.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ApprovalSagaArchitectureTests
{
    private const string NeutralOperatorNamespace = "GeoAssets.NeutralOperator";

    [Theory]
    [InlineData(typeof(AssetMutationBatchApplyHandler))]
    [InlineData(typeof(AssetMutationApplyHostExtensions))]
    [InlineData(typeof(KernelAssetMutationApplier))]
    public void TheKernelsApplyConsumer_IsAKernelType_InTheServerAssembly_OutsideTheModuleQuarantine(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        type.Assembly.Should().BeSameAs(typeof(GeoAssetsRestApiExtensions).Assembly);
        KernelReferenceRatchet.IsKernelType(type).Should().BeTrue("a module type would disappear with the module; the kernel must keep answering batches");
        type.Namespace.Should().Be("GeoAssets.Server");
    }

    [Fact]
    public void TheKernelsApplyConsumer_ReferencesNoVerticalType_NotInItsSignatureNorItsFieldsNorItsInterfaces()
    {
        var violations = KernelReferenceScanner.FindForbiddenReferences(
            [typeof(AssetMutationBatchApplyHandler), typeof(AssetMutationApplyHostExtensions), typeof(KernelAssetMutationApplier)],
            KernelReferenceRatchet.IsForbidden);

        violations.Should().BeEmpty("the handler answers any vertical's batch through SourceVerticalId alone (ADR-001 invariant I1)");
    }

    [Fact]
    public void TheKernelsApplyConsumer_NamesNoVerticalByIdInItsConstants()
    {
        AssetMutationBatchApplyHandler.ConsumerName.Should().Be("kernel.asset-mutation-apply");
        AssetMutationBatchApplyHandler.ConsumerKey.Should().Be("AssetMutationApply");
        typeof(AssetMutationBatchApplyHandler).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Should().NotContain(value => value.Contains("neutral", StringComparison.OrdinalIgnoreCase) || value.Contains("occupancy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithEveryModuleDisabled_TheKernelStillHostsTheApplyConsumer_AndNoVerticalSagaConsumer()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var routes = provider.GetServices<InProcessIntegrationRoute>().Select(route => (route.MessageType, route.ConsumerName)).ToList();
        routes.Should().Contain((IntegrationEventTypes.AssetMutationBatchRequestedV1, AssetMutationBatchApplyHandler.ConsumerName));
        routes.Should().NotContain(route => route.MessageType == IntegrationEventTypes.AssetMutationBatchAppliedV1
            || route.MessageType == IntegrationEventTypes.AssetMutationBatchRejectedV1, "answers are consumed by the vertical that asked");
        var registered = provider.GetRequiredService<IServiceProviderIsService>();
        registered.IsService(typeof(IIntegrationEventHandler<AssetMutationBatchRequestedEvent>)).Should().BeTrue("the kernel answers batches whatever modules it hosts");
        registered.IsService(typeof(IIntegrationEventHandler<AssetBatchAppliedEvent>)).Should().BeFalse();
        registered.IsService(typeof(IIntegrationEventHandler<AssetMutationBatchRejectedEvent>)).Should().BeFalse();
        provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Should().NotContain(service => service is OccupancyApplySagaSweeper);
    }

    [Fact]
    public async Task WithTheNeutralOperatorModule_TheKernelHostsBothHalvesOfTheSaga()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        provider.GetService<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>().Should().BeOfType<AssetMutationBatchApplyHandler>();
        provider.GetService<IIntegrationEventHandler<AssetBatchAppliedEvent>>().Should().BeOfType<OccupancyApplyAppliedHandler>();
        provider.GetService<IIntegrationEventHandler<AssetMutationBatchRejectedEvent>>().Should().BeOfType<OccupancyApplyRejectedHandler>();
        provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OccupancyApplySagaSweeper>().Should().ContainSingle();
    }

    [Fact]
    public void NoKernelType_NamesTheNeutralOperatorNamespace_AsTheApplyConsumersReplyTypesAreTheCoreContracts()
    {
        typeof(AssetBatchAppliedEvent).Namespace.Should().NotStartWith(NeutralOperatorNamespace, "the replies are the core's contracts, shared by every vertical");
        typeof(AssetMutationBatchRejectedEvent).Namespace.Should().NotStartWith(NeutralOperatorNamespace);
    }

    [Fact]
    public async Task WithTheModule_TheRoutesHoldTheApprovalSagasRetryRoute_Exactly_AndTheCutOverSnapshotNone()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        var withModule = RouteInventory.Describe(services.GetRequiredService<EndpointDataSource>().Endpoints);
        var snapshot = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt"))).Where(line => line.Length > 0).ToArray();

        withModule.Where(line => line.Contains("saga", StringComparison.OrdinalIgnoreCase)).Should().ContainSingle(
            "XD01-383 adds the retry route and nothing else").Which.Should().Be("POST /api/workflow/service-orders/{id}/occupancy-saga/retry | none");
        snapshot.Where(line => line.Contains("saga", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty(
            "since the XD01-384 cut-over the kernel's default composition hosts none of the NeutralOperator module's routes");
    }
}
