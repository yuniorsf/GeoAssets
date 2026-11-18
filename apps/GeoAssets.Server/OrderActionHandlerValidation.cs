using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// Fails the host's startup when two <see cref="IOrderActionHandler"/>s claim the same (order type, action) pair
/// (XD01-360) — checked against the registry as the host starts, i.e. after its database-persisted order types are loaded.
/// </summary>
public sealed class OrderActionHandlerValidation(IServiceScopeFactory scopes, OrderTypeRegistry orderTypes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            new OrderActionHandlerRegistry(scope.ServiceProvider.GetServices<IOrderActionHandler>())
                .EnsureUnambiguous(orderTypes.All);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
