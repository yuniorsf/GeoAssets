using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

public class OrderTargetQueryTests
{
    // ── Defaults and shape ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_OnlyOrderId_UsesDocumentedDefaults()
    {
        var query = new OrderTargetQuery("ord-1");

        query.OrderId.Should().Be("ord-1");
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(50);
        query.SortBy.Should().BeNull();
        query.Descending.Should().BeFalse();
        query.Status.Should().BeNull();
        query.ConflictLevel.Should().BeNull();
        query.Skip.Should().Be(0);
    }

    [Fact]
    public void Constructor_AllArguments_ExposesThemUnchanged()
    {
        var query = new OrderTargetQuery(
            "ord-1",
            page: 3,
            pageSize: 25,
            sortBy: OrderTargetSortKeys.Alias,
            descending: true,
            status: OrderTargetStatus.Done,
            conflictLevel: OrderTargetConflictLevel.Blocked);

        query.Page.Should().Be(3);
        query.PageSize.Should().Be(25);
        query.SortBy.Should().Be("alias");
        query.Descending.Should().BeTrue();
        query.Status.Should().Be(OrderTargetStatus.Done);
        query.ConflictLevel.Should().Be(OrderTargetConflictLevel.Blocked);
        query.Skip.Should().Be(50);
    }

    [Fact]
    public void Limits_AreFiftyByDefaultAndTwoHundredAtMost()
    {
        OrderTargetQuery.DefaultPageSize.Should().Be(50);
        OrderTargetQuery.MaxPageSize.Should().Be(200);
    }

    [Fact]
    public void Equals_SameValues_AreEqual()
    {
        new OrderTargetQuery("ord-1", 2, 10, "status", true, "Done", OrderTargetConflictLevel.Warning)
            .Should().Be(new OrderTargetQuery("ord-1", 2, 10, "status", true, "Done", OrderTargetConflictLevel.Warning));
    }

    [Fact]
    public void Json_RoundTripThroughSharedSerializerOptions_PreservesEveryMember()
    {
        var query = new OrderTargetQuery("ord-1", 2, 10, "status", true, "Done", OrderTargetConflictLevel.Warning);

        var json = JsonSerializer.Serialize(query, GeoJsonSerializer.Options);
        var back = JsonSerializer.Deserialize<OrderTargetQuery>(json, GeoJsonSerializer.Options);

        back.Should().Be(query);
    }

    [Fact]
    public void Json_TGT_G3_PageSizeAboveLimitInPayload_ThrowsArgumentOutOfRangeException()
    {
        var act = () => JsonSerializer.Deserialize<OrderTargetQuery>("""{"orderId":"ord-1","pageSize":201}""", GeoJsonSerializer.Options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── TGT-G1 (order id) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_TGT_G1_BlankOrderId_ThrowsArgumentException(string? blank)
    {
        var act = () => new OrderTargetQuery(blank!);

        act.Should().Throw<ArgumentException>().WithParameterName("orderId");
    }

    [Fact]
    public void With_TGT_G1_BlankOrderId_ThrowsArgumentException()
    {
        var query = new OrderTargetQuery("ord-1");

        var act = () => query with { OrderId = "" };

        act.Should().Throw<ArgumentException>();
    }

    // ── TGT-G2 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_TGT_G2_PageBelowOne_ThrowsArgumentOutOfRangeException(int invalid)
    {
        var act = () => new OrderTargetQuery("ord-1", page: invalid);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("page");
    }

    [Fact]
    public void With_TGT_G2_PageBelowOne_ThrowsArgumentOutOfRangeException()
    {
        var query = new OrderTargetQuery("ord-1");

        var act = () => query with { Page = 0 };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("page");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10_000)]
    public void Constructor_TGT_G2_PageOneOrAbove_IsAccepted(int page)
    {
        new OrderTargetQuery("ord-1", page: page).Page.Should().Be(page);
    }

    // ── TGT-G3 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void Constructor_TGT_G3_PageSizeOutsideOneToTwoHundred_ThrowsArgumentOutOfRangeException(int invalid)
    {
        var act = () => new OrderTargetQuery("ord-1", pageSize: invalid);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("pageSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void With_TGT_G3_PageSizeOutsideOneToTwoHundred_ThrowsArgumentOutOfRangeException(int invalid)
    {
        var query = new OrderTargetQuery("ord-1");

        var act = () => query with { PageSize = invalid };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("pageSize");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(200)]
    public void Constructor_TGT_G3_PageSizeAtTheBoundaries_IsAccepted(int pageSize)
    {
        new OrderTargetQuery("ord-1", pageSize: pageSize).PageSize.Should().Be(pageSize);
    }

    [Theory]
    [InlineData(1, 200, 0)]
    [InlineData(2, 200, 200)]
    [InlineData(4, 25, 75)]
    public void Skip_IsPageMinusOneTimesPageSize(int page, int pageSize, int expected)
    {
        new OrderTargetQuery("ord-1", page, pageSize).Skip.Should().Be(expected);
    }

    // ── TGT-B1 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("alias", "alias")]
    [InlineData("status", "status")]
    [InlineData("conflictLevel", "conflictLevel")]
    [InlineData("addedAt", "addedAt")]
    public void Resolve_TGT_B1_RecognizedSortKey_IsKept(string sortBy, string expected)
    {
        OrderTargetSortKeys.Resolve(sortBy).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("featureId")]
    [InlineData("note")]
    [InlineData("Alias")]
    [InlineData("conflictlevel")]
    [InlineData("alias; DROP TABLE x")]
    public void Resolve_TGT_B1_NullOrUnknownSortKey_FallsBackToFeatureId(string? sortBy)
    {
        OrderTargetSortKeys.Resolve(sortBy).Should().Be("featureId");
    }

    [Fact]
    public void Constructor_TGT_B1_UnknownSortKey_IsAcceptedNotRejected()
    {
        var query = new OrderTargetQuery("ord-1", sortBy: "no-such-key");

        query.SortBy.Should().Be("no-such-key");
        OrderTargetSortKeys.Resolve(query.SortBy).Should().Be(OrderTargetSortKeys.FeatureId);
    }

    [Fact]
    public void SortKeyConstants_TGT_B1_HaveTheDocumentedWireValues()
    {
        OrderTargetSortKeys.Alias.Should().Be("alias");
        OrderTargetSortKeys.Status.Should().Be("status");
        OrderTargetSortKeys.ConflictLevel.Should().Be("conflictLevel");
        OrderTargetSortKeys.AddedAt.Should().Be("addedAt");
        OrderTargetSortKeys.FeatureId.Should().Be("featureId");
    }
}
