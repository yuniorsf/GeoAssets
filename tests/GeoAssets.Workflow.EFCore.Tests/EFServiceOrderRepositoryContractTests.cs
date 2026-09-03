using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="ServiceOrderRepositoryContractTests"/> suite against
/// <see cref="EFServiceOrderRepository"/> unwrapped (no <c>ValidatingServiceOrderRepository</c>
/// decorator) — proves the implementation honors the contract on its own, matching its own
/// doc comment's claim of being safe to use unwrapped. See XD01-27.
/// </summary>
public sealed class EFServiceOrderRepositoryContractTests : ServiceOrderRepositoryContractTests, IDisposable
{
    private readonly SqliteFixture _fixture = new();

    protected override IServiceOrderRepository CreateRepository() =>
        new EFServiceOrderRepository(_fixture.Context);

    public void Dispose() => _fixture.Dispose();
}
