using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Xunit;

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

    /// <summary>
    /// Pins what this class exists to prove: the suite runs against the EF repository itself,
    /// not a decorated one. Declaring a test directly on the class also keeps it recognized as
    /// a test class by the analyzers, so it stays public and discoverable by xUnit (XD01-303).
    /// </summary>
    [Fact]
    public void CreateRepository_ReturnsTheUnwrappedEfRepository() =>
        CreateRepository().Should().BeOfType<EFServiceOrderRepository>();

    public void Dispose() => _fixture.Dispose();
}
