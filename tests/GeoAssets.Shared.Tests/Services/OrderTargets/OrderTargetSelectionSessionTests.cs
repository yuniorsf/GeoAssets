using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.OrderTargetPreviewFactory;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The preview-then-apply conversation behind the preview panel (XD01-312).</summary>
public sealed class OrderTargetSelectionSessionTests
{
    private const string OrderId = "o1";

    private static readonly WorkflowPrincipal _principal = new("u1", "org-1", [], [], []);

    private static readonly OrderTargetSelectionPlan _plan = new(
        "polygon-fence",
        new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = new double[1][][] },
        IsFence: true,
        IdentifierCount: 0);

    /// <summary>Every call gets its own completion source, so a test decides when and how each one finishes.</summary>
    private sealed class ScriptedSelectionService : IOrderTargetSelectionService
    {
        public List<(TargetSelectionRequest Request, WorkflowPrincipal Principal, CancellationToken Token, TaskCompletionSource<TargetSelectionPreview> Completion)> Previews { get; } = [];
        public List<(TargetSelectionRequest Request, CancellationToken Token, TaskCompletionSource<TargetSelectionResult> Completion)> Applies { get; } = [];

        public Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<TargetSelectionPreview>();
            Previews.Add((request, principal, ct, completion));
            return completion.Task;
        }

        public Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<TargetSelectionResult>();
            Applies.Add((request, ct, completion));
            return completion.Task;
        }
    }

    private static (OrderTargetSelectionSession Sut, ScriptedSelectionService Service) Create()
    {
        var service = new ScriptedSelectionService();
        return (new OrderTargetSelectionSession(service, OrderId, _plan, _principal), service);
    }

    private static Task PreviewedAsync(OrderTargetSelectionSession sut, ScriptedSelectionService service, TargetSelectionPreview preview, int page = 1)
    {
        var previewing = sut.PreviewAsync(page, CancellationToken.None);
        service.Previews[^1].Completion.SetResult(preview);
        return previewing;
    }

    [Fact]
    public void Constructor_GuardsItsArguments()
    {
        var service = new ScriptedSelectionService();

        FluentActions.Invoking(() => new OrderTargetSelectionSession(null!, OrderId, _plan, _principal)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetSelectionSession(service, " ", _plan, _principal)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new OrderTargetSelectionSession(service, OrderId, null!, _principal)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetSelectionSession(service, OrderId, _plan, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Preview_SendsThePlanForThatOrderAndPage_NeverAcceptingTruncation_AndShowsTheResult()
    {
        var (sut, service) = Create();

        var previewing = sut.PreviewAsync(3, CancellationToken.None);
        sut.IsPreviewing.Should().BeTrue();
        sut.IsBusy.Should().BeTrue();
        sut.CanApply.Should().BeFalse();
        var call = service.Previews.Should().ContainSingle().Subject;
        call.Completion.SetResult(Preview(matched: 30, page: 3));
        await previewing;

        call.Request.Should().Match<TargetSelectionRequest>(r => r.OrderId == OrderId && r.StrategyId == _plan.StrategyId
            && r.Page == 3 && r.PageSize == OrderTargetSelectionSession.PreviewPageSize && !r.AcceptTruncation);
        call.Request.Parameters.Should().BeSameAs(_plan.Parameters);
        call.Principal.Should().BeSameAs(_principal);
        sut.PreviewPage.Should().Be(3);
        sut.Summary!.ToAdd.Should().Be(30);
        sut.IsPreviewing.Should().BeFalse();
        sut.CanApply.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_PageBelowOne_AsksForTheFirstPage()
    {
        var (sut, service) = Create();

        await PreviewedAsync(sut, service, Preview(), page: 0);

        service.Previews[0].Request.Page.Should().Be(1);
        sut.PreviewPage.Should().Be(1);
    }

    [Fact]
    public async Task Preview_Failure_KeepsThePreviousSummary_AndSaysWhy()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 7));

        var previewing = sut.PreviewAsync(2, CancellationToken.None);
        service.Previews[^1].Completion.SetException(new SelectionTooLargeException(50_001, 50_000));
        await previewing;

        sut.Summary!.ToAdd.Should().Be(7);
        sut.PreviewPage.Should().Be(1);
        sut.ErrorKey.Should().Be("orders.fromSelection.error.tooLarge");
        sut.IsPreviewing.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_OvertakenByANewerOne_IsDropped_WhicheverFinishesLast()
    {
        var (sut, service) = Create();

        var first = sut.PreviewAsync(1, CancellationToken.None);
        var second = sut.PreviewAsync(2, CancellationToken.None);
        service.Previews[1].Completion.SetResult(Preview(matched: 20, page: 2));
        await second;
        service.Previews[0].Completion.SetException(new HttpRequestException("late"));
        await first;

        sut.PreviewPage.Should().Be(2);
        sut.ErrorKey.Should().BeNull();
        sut.IsPreviewing.Should().BeFalse();

        var third = sut.PreviewAsync(3, CancellationToken.None);
        var fourth = sut.PreviewAsync(4, CancellationToken.None);
        service.Previews[3].Completion.SetResult(Preview(matched: 40, page: 4));
        await fourth;
        service.Previews[2].Completion.SetResult(Preview(matched: 1, page: 3));
        await third;

        sut.PreviewPage.Should().Be(4);
        sut.Summary!.ToAdd.Should().Be(40);
    }

    [Fact]
    public async Task Preview_CancelledByTheCaller_IsSilent()
    {
        var (sut, service) = Create();
        using var cts = new CancellationTokenSource();

        var previewing = sut.PreviewAsync(1, cts.Token);
        await cts.CancelAsync();
        service.Previews[0].Completion.SetCanceled(cts.Token);
        await previewing;

        sut.ErrorKey.Should().BeNull();
        sut.Summary.Should().BeNull();
        service.Previews[0].Token.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Preview_ACancellationTheCallerDidNotAskFor_IsAFailure()
    {
        var (sut, service) = Create();

        var previewing = sut.PreviewAsync(1, CancellationToken.None);
        service.Previews[0].Completion.SetException(new TaskCanceledException("timeout"));
        await previewing;

        sut.ErrorKey.Should().Be("orders.fromSelection.error.failed");
    }

    [Fact]
    public async Task TruncationAcceptance_IsKeptAcrossPagesWithTheSameNumbers_AndResetWhenTheyChange()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 30, truncated: 10));
        sut.CanApply.Should().BeFalse();

        sut.SetTruncationAccepted(true);
        sut.CanApply.Should().BeTrue();

        await PreviewedAsync(sut, service, Preview(matched: 30, truncated: 10, page: 2), page: 2);
        sut.TruncationAccepted.Should().BeTrue();

        await PreviewedAsync(sut, service, Preview(matched: 30, truncated: 12), page: 1);
        sut.TruncationAccepted.Should().BeFalse();

        sut.SetTruncationAccepted(true);
        await PreviewedAsync(sut, service, Preview(matched: 32, truncated: 12), page: 1);
        sut.TruncationAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_BeforeAnyPreview_OrWithNothingToAdd_OrUnacceptedTruncation_SendsNothing()
    {
        var (sut, service) = Create();
        (await sut.ApplyAsync(CancellationToken.None)).Should().BeNull();

        await PreviewedAsync(sut, service, Preview(matched: 0));
        (await sut.ApplyAsync(CancellationToken.None)).Should().BeNull();

        await PreviewedAsync(sut, service, Preview(matched: 9, truncated: 3));
        (await sut.ApplyAsync(CancellationToken.None)).Should().BeNull();

        service.Applies.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_WithoutTruncation_SendsAcceptTruncationFalse_AndReturnsTheResult()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4));

        var applying = sut.ApplyAsync(CancellationToken.None);
        sut.IsApplying.Should().BeTrue();
        sut.CanApply.Should().BeFalse();
        service.Applies[0].Completion.SetResult(Result(added: 4));
        var result = await applying;

        result!.Added.Should().Be(4);
        service.Applies[0].Request.Should().Match<TargetSelectionRequest>(r => r.OrderId == OrderId && !r.AcceptTruncation && r.Page == 1);
        sut.IsApplying.Should().BeFalse();
        sut.ErrorKey.Should().BeNull();
    }

    [Fact]
    public async Task Apply_WithAcceptedTruncation_SendsAcceptTruncationTrue()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 9, truncated: 3));
        sut.SetTruncationAccepted(true);

        var applying = sut.ApplyAsync(CancellationToken.None);
        service.Applies[0].Completion.SetResult(Result(added: 6));
        await applying;

        service.Applies[0].Request.AcceptTruncation.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_ClickedTwiceWhileBusy_SendsOneRequest_AndPreviewWaitsForIt()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4));

        var applying = sut.ApplyAsync(CancellationToken.None);
        (await sut.ApplyAsync(CancellationToken.None)).Should().BeNull();
        await sut.PreviewAsync(2, CancellationToken.None);
        service.Applies[0].Completion.SetResult(Result(added: 4));
        await applying;

        service.Applies.Should().ContainSingle();
        service.Previews.Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_Failure_SaysWhy_AndAllowsAnotherTry()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4));

        var applying = sut.ApplyAsync(CancellationToken.None);
        service.Applies[0].Completion.SetException(new InvalidServiceOrderTransitionException("Completed", "Completed"));
        (await applying).Should().BeNull();

        sut.ErrorKey.Should().Be("orders.fromSelection.error.orderClosed");
        sut.IsApplying.Should().BeFalse();
        sut.CanApply.Should().BeTrue();
        service.Previews.Should().ContainSingle("only a limit change refreshes the preview");
    }

    [Fact]
    public async Task Apply_RejectedBecauseTheLimitChanged_RefreshesThePreview_AndKeepsTheReason()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4), page: 2);

        var applying = sut.ApplyAsync(CancellationToken.None);
        service.Applies[0].Completion.SetException(new SelectionLimitExceededException(3, 0, 4));
        service.Previews.Should().HaveCount(2);
        service.Previews[1].Request.Page.Should().Be(1);
        service.Previews[1].Completion.SetResult(Preview(matched: 4, truncated: 1));
        (await applying).Should().BeNull();

        sut.ErrorKey.Should().Be("orders.fromSelection.error.limitChanged");
        sut.Summary!.IsTruncated.Should().BeTrue();
        sut.TruncationAccepted.Should().BeFalse();
        sut.PreviewPage.Should().Be(1);
    }

    [Fact]
    public async Task Apply_LimitChanged_AndTheRefreshFailsToo_ShowsTheRefreshsFailure()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4));

        var applying = sut.ApplyAsync(CancellationToken.None);
        service.Applies[0].Completion.SetException(new SelectionLimitExceededException(3, 0, 4));
        service.Previews[1].Completion.SetException(new HttpRequestException("down"));
        await applying;

        sut.ErrorKey.Should().Be("orders.fromSelection.error.failed");
    }

    [Fact]
    public async Task Apply_CancelledByTheCaller_IsSilent()
    {
        var (sut, service) = Create();
        await PreviewedAsync(sut, service, Preview(matched: 4));
        using var cts = new CancellationTokenSource();

        var applying = sut.ApplyAsync(cts.Token);
        await cts.CancelAsync();
        service.Applies[0].Completion.SetCanceled(cts.Token);

        (await applying).Should().BeNull();
        sut.ErrorKey.Should().BeNull();
        sut.IsApplying.Should().BeFalse();
        service.Applies[0].Token.Should().Be(cts.Token);
    }
}
