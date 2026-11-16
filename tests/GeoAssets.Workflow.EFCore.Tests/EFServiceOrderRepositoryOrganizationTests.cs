using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-332 — <c>OrganizationId</c> is fixed when an order is created: the EF store never
/// writes it on an update, and the store as every host registers it (behind
/// <see cref="ValidatingServiceOrderRepository"/>) refuses an update that states another one.
/// </summary>
public class EFServiceOrderRepositoryOrganizationTests
{
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");

    private static ServiceOrder Order(Guid organizationId, string title, byte[]? rowVersion = null) => new()
    {
        Id = "ord-1",
        Title = title,
        RowVersion = rowVersion ?? [],
        Status = ServiceOrderStatus.Draft,
        OrderTypeId = "inspection",
        CreatedBy = "creator",
        OrganizationId = organizationId,
        CreatedAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task XD01_332_UpdateAsync_OnTheEfStoreItself_NeverWritesOrganizationId_WhateverTheOrderStates()
    {
        using var fixture = new SqliteFixture();
        var repository = new EFServiceOrderRepository(fixture.Context);
        await repository.AddAsync(Order(OrgA, "original"));

        await repository.UpdateAsync(Order(OrgB, "edited"));

        using var fresh = fixture.NewContext();
        var stored = await new EFServiceOrderRepository(fresh).GetByIdAsync("ord-1");
        stored!.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("edited");
    }

    [Fact]
    public async Task XD01_332_UpdateAsync_ThroughTheRegisteredDecorator_StatingAnotherOrganization_IsRefused_AndNothingIsWritten()
    {
        using var fixture = new SqliteFixture();
        var repository = new ValidatingServiceOrderRepository(new EFServiceOrderRepository(fixture.Context));
        await repository.AddAsync(Order(OrgA, "original"));

        var relabel = () => repository.UpdateAsync(Order(OrgB, "relabelled"));

        await relabel.Should().ThrowAsync<ServiceOrderOrganizationException>();
        using var fresh = fixture.NewContext();
        var stored = await new EFServiceOrderRepository(fresh).GetByIdAsync("ord-1");
        stored!.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("original");
    }

    [Fact]
    public async Task XD01_332_UpdateAsync_ThroughTheRegisteredDecorator_StatingNoOrganization_UpdatesAndKeepsTheStoredOne()
    {
        using var fixture = new SqliteFixture();
        var repository = new ValidatingServiceOrderRepository(new EFServiceOrderRepository(fixture.Context));
        await repository.AddAsync(Order(OrgA, "original"));

        await repository.UpdateAsync(Order(Guid.Empty, "edited"));

        using var fresh = fixture.NewContext();
        var stored = await new EFServiceOrderRepository(fresh).GetByIdAsync("ord-1");
        stored!.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("edited");
    }

    [Fact]
    public async Task XD01_332_UpdateAsync_OrderStoredWithNoOrganization_CannotBeClaimed()
    {
        using var fixture = new SqliteFixture();
        var repository = new ValidatingServiceOrderRepository(new EFServiceOrderRepository(fixture.Context));
        await repository.AddAsync(Order(Guid.Empty, "legacy"));

        var claim = () => repository.UpdateAsync(Order(OrgB, "claimed"));

        await claim.Should().ThrowAsync<ServiceOrderOrganizationException>();
        using var fresh = fixture.NewContext();
        (await new EFServiceOrderRepository(fresh).GetByIdAsync("ord-1"))!.OrganizationId.Should().Be(Guid.Empty);
    }
}
