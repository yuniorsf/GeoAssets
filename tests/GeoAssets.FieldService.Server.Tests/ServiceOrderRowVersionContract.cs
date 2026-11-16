using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence.Contracts;

/// <summary>
/// What <c>"ServiceOrders"."RowVersion"</c> must do on PostgreSQL, whichever migration history
/// built the database (XD01-323). One body, compiled into both suites that own a history for
/// <see cref="ServiceOrderDbContext"/> — this project for the isolated FieldService database, and
/// GeoAssets.Security.Tests (which links this file) for the shared GeoAssets.Server one — so
/// "behaves the same in both databases" is one set of assertions run twice, not two descriptions
/// that can drift.
///
/// Everything goes through <see cref="EFServiceOrderRepository"/>, the only code that reads or
/// compares the token, each step on a context of its own, as separate requests would.
/// </summary>
internal static class ServiceOrderRowVersionContract
{
    public static async Task AssertGuardsConcurrentWritersAsync(Func<ServiceOrderDbContext> newContext)
    {
        ArgumentNullException.ThrowIfNull(newContext);
        var id = $"rv-{Guid.NewGuid():N}"[..20];

        // A new order gets a token from the database: EF sends none (the column is IsRowVersion()).
        await using (var db = newContext())
        {
            await new EFServiceOrderRepository(db).AddAsync(new ServiceOrder
            {
                Id = id,
                Title = "Created",
                CreatedBy = "tester",
                OrderTypeId = "inspection",
                CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            });
        }
        var created = await StoredTokenAsync(newContext, id);
        created.Should().HaveLength(32, "the trigger stores the 16 bytes of a random uuid");

        // Two writers read the same version.
        var first = await ReadAsync(newContext, id);
        var second = await ReadAsync(newContext, id);
        Convert.ToHexStringLower(first.RowVersion).Should().Be(created);
        second.RowVersion.Should().Equal(first.RowVersion);

        // The first one saves: every update moves the token.
        first.Title = "Changed by the first writer";
        await UpdateAsync(newContext, first);
        var afterFirstWrite = await StoredTokenAsync(newContext, id);
        afterFirstWrite.Should().NotBe(created);

        // The second one still holds the version it read, and is refused; nothing of it is stored.
        second.Title = "Changed by the second writer";
        var stale = () => UpdateAsync(newContext, second);
        (await stale.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be(id);
        (await ReadAsync(newContext, id)).Title.Should().Be("Changed by the first writer");
        (await StoredTokenAsync(newContext, id)).Should().Be(afterFirstWrite);

        // Having read again, it may save.
        var reread = await ReadAsync(newContext, id);
        reread.Title = "Changed by the second writer, after reading again";
        await UpdateAsync(newContext, reread);
        var afterSecondWrite = await StoredTokenAsync(newContext, id);
        afterSecondWrite.Should().NotBe(afterFirstWrite);

        // A write that does not go through EF moves the token too, so EF writers notice it.
        var beforeRawWrite = await ReadAsync(newContext, id);
        await using (var db = newContext())
        {
            (await db.Database.ExecuteSqlAsync($"""UPDATE "ServiceOrders" SET "Description" = 'edited in SQL' WHERE "Id" = {id}"""))
                .Should().Be(1);
        }
        (await StoredTokenAsync(newContext, id)).Should().NotBe(afterSecondWrite);
        beforeRawWrite.Title = "Changed by a writer that missed the SQL edit";
        var staleAfterRawWrite = () => UpdateAsync(newContext, beforeRawWrite);
        await staleAfterRawWrite.Should().ThrowAsync<ServiceOrderConcurrencyException>();
    }

    private static async Task<ServiceOrder> ReadAsync(Func<ServiceOrderDbContext> newContext, string id)
    {
        await using var db = newContext();
        var order = await new EFServiceOrderRepository(db).GetByIdAsync(id);
        return order.Should().BeOfType<ServiceOrder>().Subject;
    }

    private static async Task UpdateAsync(Func<ServiceOrderDbContext> newContext, ServiceOrder order)
    {
        await using var db = newContext();
        await new EFServiceOrderRepository(db).UpdateAsync(order);
    }

    /// <summary>The stored token in hexadecimal, read with SQL so that nothing EF tracks is involved.</summary>
    private static async Task<string> StoredTokenAsync(Func<ServiceOrderDbContext> newContext, string id)
    {
        await using var db = newContext();
        return await db.Database
            .SqlQuery<string>($"""SELECT encode("RowVersion", 'hex') AS "Value" FROM "ServiceOrders" WHERE "Id" = {id}""")
            .SingleAsync();
    }
}
