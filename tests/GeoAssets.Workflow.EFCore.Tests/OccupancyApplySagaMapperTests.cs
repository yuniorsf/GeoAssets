using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>The unrestored feature ids of <see cref="OccupancyApplySagaMapper"/> (XD01-392): null on the row means none.</summary>
public sealed class OccupancyApplySagaMapperTests
{
    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000392");
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));

    private static OccupancyApplySaga Saga(params string[] unrestored) => new()
    {
        BatchId = Guid.NewGuid(),
        Attempt = 1,
        OrderId = "ord-1",
        OrderTypeId = "pole-work",
        OrganizationId = Organization,
        PreviousStatus = "Completed",
        ApprovedStatus = "Approved",
        RequestedBy = "approver-1",
        RequestedAt = Clock.GetUtcNow(),
        LastRequestedAt = Clock.GetUtcNow(),
        State = OccupancyApplySagaState.Reverted,
        CompletedAt = Clock.GetUtcNow(),
        UnrestoredFeatureIds = unrestored,
        Batch = AssetMutationBatchRequestedEvent.Create("catalog", Organization, "tests", "approver-1", [], Clock, sourceOrderId: "ord-1"),
    };

    [Fact]
    public void ToRecord_NoUnrestoredFeatureIds_StoresNull()
    {
        OccupancyApplySagaMapper.ToRecord(Saga()).UnrestoredFeatureIdsJson.Should().BeNull();
    }

    [Fact]
    public void ToRecord_UnrestoredFeatureIds_StoresAJsonArrayInOrder()
    {
        OccupancyApplySagaMapper.ToRecord(Saga("pole-1", "pole-2")).UnrestoredFeatureIdsJson.Should().Be("""["pole-1","pole-2"]""");
    }

    [Fact]
    public void ToDomain_ANullColumn_ReadsAsEmpty_LikeARowWrittenBeforeTheColumnExisted()
    {
        var record = OccupancyApplySagaMapper.ToRecord(Saga());
        record.UnrestoredFeatureIdsJson = null;

        OccupancyApplySagaMapper.ToDomain(record).UnrestoredFeatureIds.Should().BeEmpty();
    }

    [Fact]
    public void ToDomain_AfterToRecord_PreservesTheUnrestoredFeatureIds()
    {
        var saga = Saga("pole-1", "pole-9");

        var read = OccupancyApplySagaMapper.ToDomain(OccupancyApplySagaMapper.ToRecord(saga));

        read.UnrestoredFeatureIds.Should().Equal("pole-1", "pole-9");
    }

    [Fact]
    public void ToDomain_ARoundTripWithNone_StaysEmpty()
    {
        OccupancyApplySagaMapper.ToDomain(OccupancyApplySagaMapper.ToRecord(Saga())).UnrestoredFeatureIds.Should().BeEmpty();
    }

    [Fact]
    public void ToDomain_MalformedUnrestoredJson_ThrowsInvalidOperation()
    {
        var record = OccupancyApplySagaMapper.ToRecord(Saga("pole-1"));
        record.UnrestoredFeatureIdsJson = "{not json";

        var act = () => OccupancyApplySagaMapper.ToDomain(record);

        act.Should().Throw<InvalidOperationException>().WithMessage("*unrestored feature ids*");
    }

    [Fact]
    public void View_From_CarriesTheUnrestoredFeatureIds()
    {
        OccupancyApplySagaView.From(Saga("pole-3")).UnrestoredFeatureIds.Should().Equal("pole-3");
        OccupancyApplySagaView.From(Saga()).UnrestoredFeatureIds.Should().BeEmpty();
    }
}
