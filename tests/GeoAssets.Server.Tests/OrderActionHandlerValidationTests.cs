using FluentAssertions;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>The host refuses to start when two order action handlers claim the same (order type, action) pair (XD01-360).</summary>
public sealed class OrderActionHandlerValidationTests
{
    private abstract class ApproveHandler(string orderTypeId) : IOrderActionHandler
    {
        public bool CanHandle(OrderType? orderType, OrderActionLog entry) =>
            string.Equals(orderType?.Id, orderTypeId, StringComparison.Ordinal) && entry.Action == OrderActionType.Approve;

        public Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OrderActionOutcome.Applied);
    }

    private sealed class InspectionApproveHandler() : ApproveHandler("inspection");

    private sealed class AnotherInspectionApproveHandler() : ApproveHandler("inspection");

    private sealed class MaintenanceApproveHandler() : ApproveHandler("maintenance");

    private static OrderActionHandlerValidation Build(params Type[] handlerTypes)
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        foreach (var type in handlerTypes) services.AddScoped(typeof(IOrderActionHandler), type);
        services.AddHostedService<OrderActionHandlerValidation>();
        return services.BuildServiceProvider().GetServices<IHostedService>().OfType<OrderActionHandlerValidation>().Single();
    }

    [Fact]
    public async Task StartAsync_TwoHandlersClaimingTheSamePair_FailsStartup()
    {
        var validation = Build(typeof(InspectionApproveHandler), typeof(AnotherInspectionApproveHandler));

        var act = () => validation.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*InspectionApproveHandler*AnotherInspectionApproveHandler*Approve*inspection*");
    }

    [Fact]
    public Task StartAsync_UnambiguousHandlers_Starts()
    {
        var validation = Build(typeof(InspectionApproveHandler), typeof(MaintenanceApproveHandler));

        var act = () => validation.StartAsync(CancellationToken.None);

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task StartAsync_NoHandlers_Starts()
    {
        var validation = Build();

        var act = () => validation.StartAsync(CancellationToken.None);

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task StopAsync_Completes()
    {
        var validation = Build();

        var act = () => validation.StopAsync(CancellationToken.None);

        return act.Should().NotThrowAsync();
    }
}
