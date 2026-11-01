using FluentAssertions;
using GeoAssets.Domain.NeutralPole.UnauthorizedCableNotice;
using Xunit;

namespace GeoAssets.Domain.NeutralPole.Tests.UnauthorizedCableNotice;

public class CaboAReveliaNoticeTests
{
    private static readonly DateTimeOffset Detected = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewNotice_StartsInDetectedStatus()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);

        notice.Status.Should().Be(CaboAReveliaStatus.Detected);
        notice.NoticeIssuedAt.Should().BeNull();
        notice.NoticeDeadline.Should().BeNull();
    }

    [Fact]
    public void IssueNotice_FromDetected_MovesToNoticeIssuedAndSetsThirtyDayDeadline()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        var issuedAt = Detected + TimeSpan.FromDays(2);

        notice.IssueNotice(issuedAt);

        notice.Status.Should().Be(CaboAReveliaStatus.NoticeIssued);
        notice.NoticeDeadline.Should().Be(issuedAt + TimeSpan.FromDays(30));
    }

    [Fact]
    public void IssueNotice_WhenNotInDetectedStatus_Throws()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);

        var act = () => notice.IssueNotice(Detected);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EvaluateExpiry_BeforeDeadline_StaysNoticeIssued()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);

        notice.EvaluateExpiry(Detected + TimeSpan.FromDays(29));

        notice.Status.Should().Be(CaboAReveliaStatus.NoticeIssued);
    }

    [Fact]
    public void EvaluateExpiry_PastThirtyDayDeadline_BecomesExpired()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);

        notice.EvaluateExpiry(Detected + TimeSpan.FromDays(31));

        notice.Status.Should().Be(CaboAReveliaStatus.Expired);
    }

    [Fact]
    public void EvaluateExpiry_WhenAlreadyResolved_IsANoOp()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);
        notice.Resolve(Detected + TimeSpan.FromDays(5));

        notice.EvaluateExpiry(Detected + TimeSpan.FromDays(60));

        notice.Status.Should().Be(CaboAReveliaStatus.Resolved);
    }

    [Fact]
    public void Resolve_FromNoticeIssued_Succeeds()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);
        var resolvedAt = Detected + TimeSpan.FromDays(10);

        notice.Resolve(resolvedAt);

        notice.Status.Should().Be(CaboAReveliaStatus.Resolved);
        notice.ResolvedAt.Should().Be(resolvedAt);
    }

    [Fact]
    public void Resolve_AfterExpiry_IsStillAllowed()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);
        notice.IssueNotice(Detected);
        notice.EvaluateExpiry(Detected + TimeSpan.FromDays(31));

        notice.Resolve(Detected + TimeSpan.FromDays(40));

        notice.Status.Should().Be(CaboAReveliaStatus.Resolved, "a late fix still ends the violation and should be recorded, not rejected because the window already lapsed");
    }

    [Fact]
    public void Resolve_FromDetected_Throws()
    {
        var notice = new CaboAReveliaNotice("pole-1", Detected);

        var act = () => notice.Resolve(Detected);

        act.Should().Throw<InvalidOperationException>("a notice must be issued before it can be resolved");
    }

    [Fact]
    public void Constructor_NullOrWhitespacePoleId_Throws()
    {
        var act = () => new CaboAReveliaNotice(" ", Detected);

        act.Should().Throw<ArgumentException>();
    }
}
