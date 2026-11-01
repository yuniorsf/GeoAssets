using System.Globalization;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;
using Xunit;

namespace GeoAssets.Workflow.Tests.SlaGuard;

/// <summary>
/// XD01-226's ACs: every documented matrix row produces the exact deadline it specifies, and a
/// breach raises a typed domain event.
/// </summary>
public class SlaGuardEngineTests
{
    public static readonly TheoryData<string, string, TimeSpan> MatrixRows = new()
    {
        { "EmergencyRestoration", "Urban", TimeSpan.FromHours(1) },
        { "EmergencyRestoration", "Rural", TimeSpan.FromHours(3) },
        { "UrgentReconnection", "Urban", TimeSpan.FromHours(24) },
        { "SurgeInspection", "Urban", TimeSpan.FromDays(2) },
        { "MmgdSurvey", "Rural", TimeSpan.FromDays(7) },
        { "IsapLeak", "Urban", TimeSpan.FromHours(4) },
    };

    private static SlaGuardEngine BuildEngine() => new(
        MatrixRows.Select(row => new SlaMatrixRow((string)row[0]!, (string)row[1]!, (TimeSpan)row[2]!)));

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void ComputeDeadline_EveryDocumentedRow_ProducesExactMaxDuration(string category, string setting, TimeSpan maxDuration)
    {
        var engine = BuildEngine();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        engine.ComputeDeadline(category, setting, startedAt).Should().Be(startedAt + maxDuration);
    }

    [Fact]
    public void ComputeDeadline_UndocumentedCombination_ThrowsKeyNotFoundException()
    {
        var engine = BuildEngine();

        var act = () => engine.ComputeDeadline("EmergencyRestoration", "Offshore", TimeProvider.System.GetUtcNow());

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void CheckBreach_BeforeDeadline_ReturnsNull()
    {
        var engine = BuildEngine();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var now = startedAt + TimeSpan.FromMinutes(30); // EmergencyRestoration/Urban allows 1h

        engine.CheckBreach("order-1", "EmergencyRestoration", "Urban", startedAt, now).Should().BeNull();
    }

    [Fact]
    public void CheckBreach_ExactlyAtDeadline_ReturnsNull()
    {
        // Boundary-inclusive for the deadline itself — breached only strictly after it.
        var engine = BuildEngine();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deadline = startedAt + TimeSpan.FromHours(1);

        engine.CheckBreach("order-1", "EmergencyRestoration", "Urban", startedAt, deadline).Should().BeNull();
    }

    [Fact]
    public void CheckBreach_AfterDeadline_RaisesTypedBreachEventWithCorrectOverage()
    {
        var engine = BuildEngine();
        var startedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deadline = startedAt + TimeSpan.FromHours(1);
        var now = deadline + TimeSpan.FromMinutes(15);

        var breach = engine.CheckBreach("order-1", "EmergencyRestoration", "Urban", startedAt, now);

        breach.Should().NotBeNull();
        breach!.OrderId.Should().Be("order-1");
        breach.Deadline.Should().Be(deadline);
        breach.DetectedAt.Should().Be(now);
        breach.Overage.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void AttachDeadline_StampsDeadlineAndCategoryOntoOrderAttributes()
    {
        var engine = BuildEngine();
        var order = new ServiceOrder();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        engine.AttachDeadline(order, "EmergencyRestoration", "Urban", timeProvider);

        order.Attributes[SlaGuardEngine.ServiceCategoryAttributeKey].Should().Be("EmergencyRestoration");
        order.Attributes[SlaGuardEngine.TerritorialSettingAttributeKey].Should().Be("Urban");
        DateTimeOffset.Parse(order.Attributes[SlaGuardEngine.DeadlineAttributeKey], CultureInfo.InvariantCulture).Should().Be(fixedNow + TimeSpan.FromHours(1));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
