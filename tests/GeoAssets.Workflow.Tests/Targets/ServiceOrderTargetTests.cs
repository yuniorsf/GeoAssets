using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

public class ServiceOrderTargetTests
{
    private static readonly OrderTargetActivity[] Catalog =
    [
        new("inspect", "Inspect"),
        new("replace", "Replace"),
        new("clean", "Clean"),
    ];

    // ── Shape ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AllMembersSet_ExposesThemUnchanged()
    {
        var addedAt = new DateTime(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc);
        var organizationId = Guid.Parse("11111111-2222-4333-8444-555555555555");

        var target = new ServiceOrderTarget
        {
            Id = "tgt-1",
            OrderId = "ord-1",
            FeatureId = "feat-1",
            AssetTypeId = "pole",
            Alias = "P-1",
            ActivityIds = ["inspect"],
            Status = OrderTargetStatus.InProgress,
            ConflictLevel = OrderTargetConflictLevel.Warning,
            Note = "gate locked",
            Longitude = -38.5,
            Latitude = -12.9,
            AddedAt = addedAt,
            AddedBy = "user-1",
            OrganizationId = organizationId,
            Version = 3,
        };

        target.Id.Should().Be("tgt-1");
        target.OrderId.Should().Be("ord-1");
        target.FeatureId.Should().Be("feat-1");
        target.AssetTypeId.Should().Be("pole");
        target.Alias.Should().Be("P-1");
        target.ActivityIds.Should().Equal("inspect");
        target.Status.Should().Be(OrderTargetStatus.InProgress);
        target.ConflictLevel.Should().Be(OrderTargetConflictLevel.Warning);
        target.Note.Should().Be("gate locked");
        target.Longitude.Should().Be(-38.5);
        target.Latitude.Should().Be(-12.9);
        target.AddedAt.Should().Be(addedAt);
        target.AddedBy.Should().Be("user-1");
        target.OrganizationId.Should().Be(organizationId);
        target.Version.Should().Be(3);
    }

    [Fact]
    public void Build_OnlyRequiredMembersSet_OptionalMembersHaveNeutralDefaults()
    {
        var target = new ServiceOrderTarget
        {
            Id = "tgt-1",
            OrderId = "ord-1",
            FeatureId = "feat-1",
            AssetTypeId = "pole",
            Status = OrderTargetStatus.Pending,
            AddedAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            AddedBy = "user-1",
            OrganizationId = Guid.Empty,
        };

        target.Alias.Should().BeNull();
        target.ActivityIds.Should().BeEmpty();
        target.ConflictLevel.Should().Be(OrderTargetConflictLevel.None);
        target.Note.Should().BeNull();
        target.Longitude.Should().BeNull();
        target.Latitude.Should().BeNull();
        target.Version.Should().Be(0);
    }

    [Fact]
    public void Json_RoundTripThroughSharedSerializerOptions_PreservesEveryMember()
    {
        var target = new ServiceOrderTargetBuilder()
            .WithActivityIds("inspect", "replace")
            .WithStatus(OrderTargetStatus.InProgress)
            .WithConflictLevel(OrderTargetConflictLevel.Blocked)
            .WithNote("gate locked")
            .WithVersion(7)
            .Build();

        var json = JsonSerializer.Serialize(target, GeoJsonSerializer.Options);
        var back = JsonSerializer.Deserialize<ServiceOrderTarget>(json, GeoJsonSerializer.Options);

        back.Should().BeEquivalentTo(target);
    }

    [Fact]
    public void Json_TGT_G1_BlankFeatureIdInPayload_ThrowsArgumentException()
    {
        var json = JsonSerializer.Serialize(new ServiceOrderTargetBuilder().Build(), GeoJsonSerializer.Options)
            .Replace("\"feat-100\"", "\" \"", StringComparison.Ordinal);

        var act = () => JsonSerializer.Deserialize<ServiceOrderTarget>(json, GeoJsonSerializer.Options);

        act.Should().Throw<ArgumentException>();
    }

    // ── TGT-G1 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_TGT_G1_BlankOrderId_ThrowsArgumentException(string? blank)
    {
        var act = () => new ServiceOrderTargetBuilder().WithOrderId(blank!).Build();

        act.Should().Throw<ArgumentException>().WithMessage("*ServiceOrderTarget.OrderId must*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_TGT_G1_BlankFeatureId_ThrowsArgumentException(string? blank)
    {
        var act = () => new ServiceOrderTargetBuilder().WithFeatureId(blank!).Build();

        act.Should().Throw<ArgumentException>().WithMessage("*ServiceOrderTarget.FeatureId must*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_TGT_G1_BlankId_ThrowsArgumentException(string? blank)
    {
        var act = () => new ServiceOrderTargetBuilder().WithId(blank!).Build();

        act.Should().Throw<ArgumentException>().WithMessage("*ServiceOrderTarget.Id must*");
    }

    [Fact]
    public void With_TGT_G1_BlankFeatureIdThroughWithExpression_ThrowsArgumentException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target with { FeatureId = " " };

        act.Should().Throw<ArgumentException>();
    }

    // ── TGT-B2 ────────────────────────────────────────────────────────────────

    [Fact]
    public void WithActivities_TGT_B2_IdsInCatalog_ReturnsCopyWithThoseIdsInGivenOrder()
    {
        var target = new ServiceOrderTargetBuilder().WithActivityIds("clean").WithVersion(4).Build();

        var changed = target.WithActivities(["replace", "inspect"], Catalog);

        changed.ActivityIds.Should().Equal("replace", "inspect");
        changed.Should().NotBeSameAs(target);
        target.ActivityIds.Should().Equal("clean");
        changed.Should().BeEquivalentTo(target, o => o.Excluding(t => t.ActivityIds));
    }

    [Fact]
    public void WithActivities_TGT_B2_IdNotInCatalog_ThrowsArgumentException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithActivities(["inspect", "paint"], Catalog);

        act.Should().Throw<ArgumentException>().WithParameterName("activityIds").WithMessage("*paint*");
    }

    [Fact]
    public void WithActivities_TGT_B2_NullElement_ThrowsArgumentException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithActivities([null!], Catalog);

        act.Should().Throw<ArgumentException>().WithParameterName("activityIds");
    }

    [Fact]
    public void WithActivities_TGT_B2_AnyIdAgainstEmptyCatalog_ThrowsArgumentException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithActivities(["inspect"], []);

        act.Should().Throw<ArgumentException>().WithParameterName("activityIds");
    }

    [Fact]
    public void WithActivities_TGT_B2_DuplicateIds_CollapseToFirstOccurrence()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var changed = target.WithActivities(["replace", "inspect", "replace", "inspect"], Catalog);

        changed.ActivityIds.Should().Equal("replace", "inspect");
    }

    [Fact]
    public void WithActivities_TGT_B2_IdsDifferingOnlyByCase_CollapseAndTakeCatalogSpelling()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var changed = target.WithActivities(["INSPECT", "Inspect"], Catalog);

        changed.ActivityIds.Should().Equal("inspect");
    }

    [Fact]
    public void WithActivities_TGT_B2_EmptyIds_ClearsActivities()
    {
        var target = new ServiceOrderTargetBuilder().WithActivityIds("clean").Build();

        var changed = target.WithActivities([], Catalog);

        changed.ActivityIds.Should().BeEmpty();
    }

    [Fact]
    public void WithActivities_TGT_B2_NullIds_ThrowsArgumentNullException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithActivities(null!, Catalog);

        act.Should().Throw<ArgumentNullException>().WithParameterName("activityIds");
    }

    [Fact]
    public void WithActivities_TGT_B2_NullCatalog_ThrowsArgumentNullException()
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithActivities(["inspect"], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("catalog");
    }

    // ── TGT-B3 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderTargetStatus.Pending, OrderTargetStatus.InProgress)]
    [InlineData(OrderTargetStatus.Pending, OrderTargetStatus.Skipped)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetStatus.Done)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetStatus.NotExecutable)]
    public void WithStatus_TGT_B3_AllowedMove_ReturnsCopyInNewStatus(string from, string to)
    {
        var target = new ServiceOrderTargetBuilder().WithStatus(from).WithVersion(2).Build();

        var changed = target.WithStatus(to);

        changed.Status.Should().Be(to);
        target.Status.Should().Be(from);
        changed.Should().BeEquivalentTo(target, o => o.Excluding(t => t.Status));
    }

    [Theory]
    [InlineData(OrderTargetStatus.Pending)]
    [InlineData(OrderTargetStatus.InProgress)]
    [InlineData(OrderTargetStatus.Done)]
    [InlineData(OrderTargetStatus.NotExecutable)]
    [InlineData(OrderTargetStatus.Skipped)]
    public void WithStatus_TGT_B3_SameStatus_IsNoOpReturningSameInstance(string status)
    {
        var target = new ServiceOrderTargetBuilder().WithStatus(status).Build();

        target.WithStatus(status).Should().BeSameAs(target);
    }

    [Theory]
    [InlineData(OrderTargetStatus.Pending, OrderTargetStatus.Done)]
    [InlineData(OrderTargetStatus.Pending, OrderTargetStatus.NotExecutable)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetStatus.Pending)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetStatus.Skipped)]
    [InlineData(OrderTargetStatus.Done, OrderTargetStatus.Pending)]
    [InlineData(OrderTargetStatus.Done, OrderTargetStatus.InProgress)]
    [InlineData(OrderTargetStatus.Done, OrderTargetStatus.NotExecutable)]
    [InlineData(OrderTargetStatus.Done, OrderTargetStatus.Skipped)]
    [InlineData(OrderTargetStatus.NotExecutable, OrderTargetStatus.Pending)]
    [InlineData(OrderTargetStatus.NotExecutable, OrderTargetStatus.InProgress)]
    [InlineData(OrderTargetStatus.NotExecutable, OrderTargetStatus.Done)]
    [InlineData(OrderTargetStatus.NotExecutable, OrderTargetStatus.Skipped)]
    [InlineData(OrderTargetStatus.Skipped, OrderTargetStatus.Pending)]
    [InlineData(OrderTargetStatus.Skipped, OrderTargetStatus.InProgress)]
    [InlineData(OrderTargetStatus.Skipped, OrderTargetStatus.Done)]
    [InlineData(OrderTargetStatus.Skipped, OrderTargetStatus.NotExecutable)]
    public void WithStatus_TGT_B3_DisallowedMove_ThrowsInvalidOperationException(string from, string to)
    {
        var target = new ServiceOrderTargetBuilder().WithStatus(from).Build();

        var act = () => target.WithStatus(to);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{from}*{to}*");
    }

    [Theory]
    [InlineData("Paused")]
    [InlineData("pending")]
    [InlineData("DONE")]
    public void WithStatus_TGT_B3_UndefinedNextStatus_ThrowsInvalidOperationException(string next)
    {
        var target = new ServiceOrderTargetBuilder().WithStatus(OrderTargetStatus.Pending).Build();

        var act = () => target.WithStatus(next);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithStatus_TGT_B3_UndefinedCurrentStatus_OnlyAllowsItself()
    {
        var target = new ServiceOrderTargetBuilder().WithStatus("Legacy").Build();

        target.WithStatus("Legacy").Should().BeSameAs(target);
        var act = () => target.WithStatus(OrderTargetStatus.InProgress);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithStatus_TGT_B3_BlankNextStatus_ThrowsArgumentException(string? blank)
    {
        var target = new ServiceOrderTargetBuilder().Build();

        var act = () => target.WithStatus(blank!);

        act.Should().Throw<ArgumentException>().WithParameterName("next");
    }

    // ── OrderTargetStatus ─────────────────────────────────────────────────────

    [Fact]
    public void All_ListsTheFiveStatusKeysInLifecycleOrder()
    {
        OrderTargetStatus.All.Should().Equal("Pending", "InProgress", "Done", "NotExecutable", "Skipped");
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("InProgress", true)]
    [InlineData("Done", true)]
    [InlineData("NotExecutable", true)]
    [InlineData("Skipped", true)]
    [InlineData("pending", false)]
    [InlineData("Completed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDefined_ReturnsTrueOnlyForExactStatusKeys(string? status, bool expected)
    {
        OrderTargetStatus.IsDefined(status).Should().Be(expected);
    }

    [Fact]
    public void CanTransition_TGT_B3_MatchesTheAllowedMoveTableForEveryPair()
    {
        var allowed = new HashSet<(string, string)>
        {
            (OrderTargetStatus.Pending, OrderTargetStatus.InProgress),
            (OrderTargetStatus.Pending, OrderTargetStatus.Skipped),
            (OrderTargetStatus.InProgress, OrderTargetStatus.Done),
            (OrderTargetStatus.InProgress, OrderTargetStatus.NotExecutable),
        };

        foreach (var from in OrderTargetStatus.All)
        {
            foreach (var to in OrderTargetStatus.All)
            {
                var expected = string.Equals(from, to, StringComparison.Ordinal) || allowed.Contains((from, to));
                OrderTargetStatus.CanTransition(from, to).Should().Be(expected, "{0} -> {1}", from, to);
            }
        }
    }

    [Fact]
    public void CanTransition_NullOperand_ReturnsFalse()
    {
        OrderTargetStatus.CanTransition(null!, OrderTargetStatus.Pending).Should().BeFalse();
        OrderTargetStatus.CanTransition(OrderTargetStatus.Pending, null!).Should().BeFalse();
    }
}
