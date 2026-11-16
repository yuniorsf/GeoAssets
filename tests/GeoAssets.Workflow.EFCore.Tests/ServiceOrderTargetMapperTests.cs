using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public sealed class ServiceOrderTargetMapperTests
{
    public static TheoryData<string?, string?, string[], OrderTargetConflictLevel, bool> RoundTripCases => new()
    {
        { null, null, [], OrderTargetConflictLevel.None, false },
        { "P-0001", "north side", ["inspect"], OrderTargetConflictLevel.Warning, true },
        { "", "", ["replace", "inspect", "clean"], OrderTargetConflictLevel.Blocked, true },
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void ToRecord_ThenToDomain_PreservesEveryMember(string? alias, string? note, string[] activityIds, OrderTargetConflictLevel conflictLevel, bool positioned)
    {
        var builder = new ServiceOrderTargetBuilder()
            .WithAlias(alias)
            .WithNote(note)
            .WithActivityIds(activityIds)
            .WithConflictLevel(conflictLevel)
            .WithStatus(OrderTargetStatus.InProgress)
            .WithVersion(6);
        var target = (positioned ? builder.WithPosition(-38.501234, -12.970987) : builder.WithoutPosition()).Build();

        var roundTripped = ServiceOrderTargetMapper.ToDomain(ServiceOrderTargetMapper.ToRecord(target));

        roundTripped.Should().BeEquivalentTo(target);
        roundTripped.ActivityIds.Should().Equal(activityIds);
    }

    [Fact]
    public void ToRecord_MapsEachDomainMemberToItsColumn()
    {
        var target = new ServiceOrderTargetBuilder()
            .WithId("tgt-1").WithOrderId("ord-1").WithFeatureId("feat-1").WithAssetTypeId("pole").WithAlias("P-1")
            .WithActivityIds("a;b", "c,d").WithStatus(OrderTargetStatus.Done).WithConflictLevel(OrderTargetConflictLevel.Blocked)
            .WithNote("note").WithPosition(1.5, -2.5).WithAddedBy("op").WithVersion(3)
            .Build();

        var record = ServiceOrderTargetMapper.ToRecord(target);

        record.Id.Should().Be("tgt-1");
        record.ServiceOrderId.Should().Be("ord-1");
        record.FeatureId.Should().Be("feat-1");
        record.AssetTypeId.Should().Be("pole");
        record.Alias.Should().Be("P-1");
        record.ActivityIdsJson.Should().Be("""["a;b","c,d"]""", "activity ids are a JSON array, so an id may itself contain a separator character");
        record.Status.Should().Be(OrderTargetStatus.Done);
        record.ConflictLevel.Should().Be(2);
        record.Note.Should().Be("note");
        record.Longitude.Should().Be(1.5);
        record.Latitude.Should().Be(-2.5);
        record.AddedAt.Should().Be(target.AddedAt);
        record.AddedBy.Should().Be("op");
        record.OrganizationId.Should().Be(target.OrganizationId);
        record.Version.Should().Be(3);
        record.ReleasedAt.Should().BeNull();
    }

    [Fact]
    public void ToRecord_WithAReleaseInstant_StoresIt_AndToDomainDropsIt()
    {
        var releasedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = new ServiceOrderTargetBuilder().Build();

        var record = ServiceOrderTargetMapper.ToRecord(target, releasedAt);

        record.ReleasedAt.Should().Be(releasedAt);
        ServiceOrderTargetMapper.ToDomain(record).Should().BeEquivalentTo(target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("""{"a":1}""")]
    [InlineData("[1,2]")]
    [InlineData("inspect;replace")]
    public void FromActivityIdsJson_AbsentOrMalformedText_ReadsAsNoActivities(string? json)
    {
        ServiceOrderTargetMapper.FromActivityIdsJson(json).Should().BeEmpty();
    }

    [Fact]
    public void FromActivityIdsJson_ArrayWithNullElements_KeepsTheStringsInOrder()
    {
        ServiceOrderTargetMapper.FromActivityIdsJson("""["b",null,"a"]""").Should().Equal("b", "a");
    }

    [Fact]
    public void ToActivityIdsJson_NullList_WritesAnEmptyArray()
    {
        ServiceOrderTargetMapper.ToActivityIdsJson(null).Should().Be("[]");
    }

    [Fact]
    public void ToUtc_NormalisesEveryKindToTheSameUtcInstant()
    {
        var utc = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        ServiceOrderTargetMapper.ToUtc(utc).Should().Be(utc).And.BeIn(DateTimeKind.Utc);
        // What SQLite hands back: the stored UTC wall-clock value with no kind.
        ServiceOrderTargetMapper.ToUtc(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)).Should().Be(utc).And.BeIn(DateTimeKind.Utc);
        ServiceOrderTargetMapper.ToUtc(utc.ToLocalTime()).Should().Be(utc).And.BeIn(DateTimeKind.Utc);
    }

    [Fact]
    public void ToDomain_AddedAtComesBackAsUtc()
    {
        var record = ServiceOrderTargetMapper.ToRecord(new ServiceOrderTargetBuilder().Build());
        record.AddedAt = DateTime.SpecifyKind(record.AddedAt, DateTimeKind.Unspecified);

        ServiceOrderTargetMapper.ToDomain(record).AddedAt.Kind.Should().Be(DateTimeKind.Utc);
    }
}
