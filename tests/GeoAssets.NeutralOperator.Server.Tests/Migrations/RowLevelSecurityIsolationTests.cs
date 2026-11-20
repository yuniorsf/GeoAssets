using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Npgsql;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Migrations;

/// <summary>
/// The row-level security of this host's database (XD01-382, AC4), observed as a non-owner application role — row-level security
/// does not restrict a table's owner: another organization sees nothing of an organization's orders, targets, action logs, dispatches
/// or pole occupancy, may not write into them, and sees only what a dispatch or an active grant lets it.
/// </summary>
public sealed class RowLevelSecurityIsolationTests
{
    private static readonly Guid Owner = TestIdentities.OwnerOrganization;
    private static readonly Guid Other = TestIdentities.OtherOrganization;
    private static readonly Guid Recipient = TestIdentities.OccupantOne;
    private static readonly Guid Stranger = new("d0d0d0d0-0000-4000-8000-000000000382");

    /// <summary>Orders <c>ord-a</c> (owner) and <c>ord-b</c> (other) with a target, a log entry and a pole occupancy each; <c>ord-a</c> is dispatched to the recipient.</summary>
    private static async Task<string> SeededAsync()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using (var owner = await KernelTables.OpenAsOwnerAsync(connectionString))
        {
            await KernelTables.InsertOrderAsync(owner, "ord-a", Owner);
            await KernelTables.InsertOrderAsync(owner, "ord-b", Other);
            await KernelTables.InsertTargetAsync(owner, "tgt-a", "ord-a", Owner);
            await KernelTables.InsertTargetAsync(owner, "tgt-b", "ord-b", Other);
            await KernelTables.InsertActionLogAsync(owner, "ord-a");
            await KernelTables.InsertActionLogAsync(owner, "ord-b");
            await KernelTables.InsertDispatchAsync(owner, "ord-a", Recipient);
            await KernelTables.InsertChangeSetAsync(owner, "ord-a", "tgt-a", Owner);
        }

        await OccupancySeed.OccupyAsync(connectionString, Owner, "pole-a", Recipient);
        await OccupancySeed.OccupyAsync(connectionString, Other, "pole-b", Recipient);
        return connectionString;
    }

    [Fact]
    public async Task AnOrganization_SeesItsOwnRows_OfEveryTable_AndNothingOfAnothers()
    {
        var connectionString = await SeededAsync();
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);

        await KernelTables.SetOrganizationAsync(scoped, Owner);
        (await KernelTables.VisibleOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisibleTargetsAsync(scoped)).Should().Equal("tgt-a");
        (await KernelTables.VisibleActionLogOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisibleDispatchOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisiblePoleOccupanciesAsync(scoped)).Should().Equal("pole-a");
        (await KernelTables.VisibleChangeSetOrdersAsync(scoped)).Should().Equal("ord-a");

        await KernelTables.SetOrganizationAsync(scoped, Other);
        (await KernelTables.VisibleOrdersAsync(scoped)).Should().Equal("ord-b");
        (await KernelTables.VisibleTargetsAsync(scoped)).Should().Equal("tgt-b");
        (await KernelTables.VisibleActionLogOrdersAsync(scoped)).Should().Equal("ord-b");
        (await KernelTables.VisibleDispatchOrdersAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisiblePoleOccupanciesAsync(scoped)).Should().Equal("pole-b");
        (await KernelTables.VisibleChangeSetOrdersAsync(scoped)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOrganizationWithNothingOfItsOwn_OrNoOrganizationAtAll_SeesNothing(bool noOrganization)
    {
        var connectionString = await SeededAsync();
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);

        if (!noOrganization) await KernelTables.SetOrganizationAsync(scoped, Stranger);

        (await KernelTables.VisibleOrdersAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisibleTargetsAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisibleActionLogOrdersAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisibleDispatchOrdersAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisiblePoleOccupanciesAsync(scoped)).Should().BeEmpty();
        (await KernelTables.VisibleChangeSetOrdersAsync(scoped)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheRecipientOfADispatch_SeesThatOrderItsTargetsLogAndDispatch_ButNothingOfTheOwnersVerticalTables()
    {
        var connectionString = await SeededAsync();
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);

        await KernelTables.SetOrganizationAsync(scoped, Recipient);

        (await KernelTables.VisibleOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisibleTargetsAsync(scoped)).Should().Equal("tgt-a");
        (await KernelTables.VisibleActionLogOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisibleDispatchOrdersAsync(scoped)).Should().Equal("ord-a");
        (await KernelTables.VisiblePoleOccupanciesAsync(scoped)).Should().BeEmpty("a notice's recipient reads the notice, not the pole owner's occupancy");
        (await KernelTables.VisibleChangeSetOrdersAsync(scoped)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AnActiveGrant_LetsTheGranteeSeeTheOwnersOrders_AnInactiveOneDoesNot(bool grantIsActive, bool expectSeen)
    {
        var connectionString = await SeededAsync();
        await using (var owner = await KernelTables.OpenAsOwnerAsync(connectionString))
            await KernelTables.InsertGrantAsync(owner, Stranger, Owner, grantIsActive);
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);

        await KernelTables.SetOrganizationAsync(scoped, Stranger);

        (await KernelTables.VisibleOrdersAsync(scoped)).Should().Equal(expectSeen ? ["ord-a"] : []);
        (await KernelTables.VisibleTargetsAsync(scoped)).Should().Equal(expectSeen ? ["tgt-a"] : []);
    }

    [Fact]
    public async Task AnotherOrganization_CannotWriteIntoAnOrganizationsOrders_NorMoveOneToItself()
    {
        var connectionString = await SeededAsync();
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);
        await KernelTables.SetOrganizationAsync(scoped, Other);

        (await KernelTables.UpdateTitleAsync(scoped, "ord-a")).Should().Be(0);
        (await KernelTables.DeleteOrderAsync(scoped, "ord-a")).Should().Be(0);
        var forgedTarget = async () => await KernelTables.InsertTargetAsync(scoped, "tgt-forged", "ord-a", Owner, "pole-9");
        var forgedOrder = async () => await KernelTables.InsertOrderAsync(scoped, "ord-forged", Owner);
        await forgedTarget.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        await forgedOrder.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        (await KernelTables.MoveOrderAsync(scoped, "ord-a", Other)).Should().Be(0);
    }

    [Fact]
    public async Task AnOrdersOrganization_IsFixed_EvenForItsOwner()
    {
        var connectionString = await SeededAsync();
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);
        await KernelTables.SetOrganizationAsync(scoped, Owner);

        var handOver = async () => await KernelTables.MoveOrderAsync(scoped, "ord-a", Other);

        await handOver.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        (await KernelTables.UpdateTitleAsync(scoped, "ord-a")).Should().Be(1, "its own order's other columns it may change");
    }
}
