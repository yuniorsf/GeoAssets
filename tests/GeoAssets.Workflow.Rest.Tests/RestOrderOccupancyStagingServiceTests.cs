using System.Net;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// The requests <see cref="RestOrderOccupancyStagingService"/> sends and how it reads the
/// answers (XD01-315) — above all that nothing about the caller's identity is on the wire, and
/// that every problem <c>code</c> of the staged-occupancy routes comes back as the exception the
/// in-process service throws.
/// </summary>
public sealed class RestOrderOccupancyStagingServiceTests
{
    private const string ViewJson = """
        {
          "orderId": "ord-1", "targetId": "tgt-1", "poleFeatureId": "pole-1", "poleOwnerOrganizationId": "0a0a0a0a-0000-4000-8000-000000000001",
          "currentVersion": 3,
          "current": { "entries": [ { "id": "0e0e0e0e-0000-4000-8000-000000000001", "poleFeatureId": "pole-1", "poleOwnerOrganizationId": "0a0a0a0a-0000-4000-8000-000000000001",
                                      "slotIndex": 1, "status": 1, "occupantOrganizationId": "0c0c0c0c-0000-4000-8000-00000000000a", "operatorCnpj": "12.345.678/0001-90",
                                      "cableDiameterMm": 12.5, "cableLoadDaN": 100, "isOccupied": true } ],
                       "occupiedCount": 1, "isOverCapacity": false, "hasUnidentifiedOccupant": false, "class": 1, "isIrregular": false },
          "staged": { "id": "5e5e5e5e-0000-4000-8000-000000000001", "orderId": "ord-1", "targetId": "tgt-1", "poleFeatureId": "pole-1", "status": 0,
                      "baselineVersion": 2, "stagedBy": "crew-1", "stagedAt": "2026-11-16T09:30:00+00:00",
                      "changes": [ { "kind": 3, "slotIndex": 1, "cableDiameterMm": 0, "cableLoadDaN": 0 },
                                   { "kind": 0, "entryId": "0e0e0e0e-0000-4000-8000-000000000002", "status": 3, "cableDiameterMm": 9.5, "cableLoadDaN": 80 } ] },
          "baselineVersion": 2, "isBaselineStale": true,
          "preview": { "entries": [], "occupiedCount": 2, "isOverCapacity": false, "hasUnidentifiedOccupant": true, "class": 4 },
          "warnings": [ "occupancy.stale-baseline" ]
        }
        """;

    private static readonly WorkflowPrincipal Principal = new(
        "user-SECRET-ID", "org-SECRET-ID", ["Administrator"], ["group-SECRET-ID"], ["serviceorders:annotate"]);

    private static HttpClient Http(BodyRecordingHandler handler) => new(handler) { BaseAddress = new Uri("http://test/api/workflow/") };

    private static RestOrderOccupancyStagingService Sut(BodyRecordingHandler handler) => new(Http(handler));

    private static BodyRecordingHandler Answering(HttpStatusCode status, string json) => new(_ => BodyRecordingHandler.Json(status, json));

    private static BodyRecordingHandler Failing(HttpStatusCode status, string code, string extraMembers = "") =>
        new(_ => BodyRecordingHandler.Problem(status, code, extraMembers));

    // ── Requests and responses ────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_GetsTheTargetsOccupancyRoute_AndReadsTheWholeView()
    {
        var handler = Answering(HttpStatusCode.OK, ViewJson);

        var view = await Sut(handler).GetAsync("ord/1", "tgt 1", Principal, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord%2F1/targets/tgt%201/occupancy");
        request.Body.Should().BeNull();
        view.Should().BeEquivalentTo(new
        {
            OrderId = "ord-1", TargetId = "tgt-1", PoleFeatureId = "pole-1", CurrentVersion = 3, BaselineVersion = (int?)2, IsBaselineStale = true,
            PoleOwnerOrganizationId = new Guid("0a0a0a0a-0000-4000-8000-000000000001"),
        });
        view.Warnings.Should().Equal(OccupancyProblemCodes.StaleBaseline);
        view.Current.Entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SlotIndex = (int?)1, Status = PoleAttachmentOccupantStatus.AuthorizedContracted, OperatorCnpj = "12.345.678/0001-90", CableDiameterMm = 12.5m,
        });
        view.Current.Class.Should().Be(OccupancyClass.Low);
        view.Preview.Should().BeEquivalentTo(new { OccupiedCount = 2, HasUnidentifiedOccupant = true, Class = OccupancyClass.Unidentified });
        view.Staged.Should().BeEquivalentTo(new
        {
            Status = OccupancyChangeSetStatus.Staged, BaselineVersion = 2, StagedBy = "crew-1",
            StagedAt = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero),
        });
        view.Staged!.Changes.Select(change => (change.Kind, change.SlotIndex, change.Status)).Should().Equal(
            (OccupancyChangeKind.MarkUnidentified, 1, null),
            (OccupancyChangeKind.Add, null, PoleAttachmentOccupantStatus.ClandestineUnauthorized));
    }

    [Fact]
    public async Task StageAsync_PutsOnlyTheChanges_WithEnumsAsNumbers_AndNothingAboutTheCaller()
    {
        var handler = Answering(HttpStatusCode.OK, ViewJson);
        var entryId = new Guid("0e0e0e0e-0000-4000-8000-000000000009");
        OccupancyChange[] changes =
        [
            new()
            {
                Kind = OccupancyChangeKind.Add, EntryId = entryId, Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
                OperatorCnpj = "98.765.432/0001-10", CableDiameterMm = 9.5m, AttachedCableFeatureId = "cable-1",
            },
            new() { Kind = OccupancyChangeKind.Remove, SlotIndex = 2 },
        ];

        var view = await Sut(handler).StageAsync("ord-1", "tgt-1", changes, Principal, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/tgt-1/occupancy-changes");
        request.Json.EnumerateObject().Select(member => member.Name).Should().Equal("changes");
        var add = request.Json.GetProperty("changes")[0];
        add.GetProperty("kind").GetInt32().Should().Be(0);
        add.GetProperty("entryId").GetGuid().Should().Be(entryId);
        add.GetProperty("status").GetInt32().Should().Be(3);
        add.GetProperty("cableDiameterMm").GetDecimal().Should().Be(9.5m);
        add.GetProperty("attachedCableFeatureId").GetString().Should().Be("cable-1");
        add.TryGetProperty("slotIndex", out _).Should().BeFalse("an entry beyond the regulated slots carries no slot index");
        request.Json.GetProperty("changes")[1].GetProperty("slotIndex").GetInt32().Should().Be(2);
        request.Body.Should().NotContain("SECRET");
        view.TargetId.Should().Be("tgt-1");
    }

    [Theory]
    [InlineData(null, "all")]
    [InlineData(OccupancyChangeSetStatus.Staged, "Staged")]
    [InlineData(OccupancyChangeSetStatus.Discarded, "Discarded")]
    [InlineData(OccupancyChangeSetStatus.Committed, "Committed")]
    public async Task GetPageAsync_AsksForThePageAndTheStatus_AndReadsTheEnvelope(OccupancyChangeSetStatus? status, string expectedStatus)
    {
        var handler = Answering(HttpStatusCode.OK, """
            { "items": [ { "id": "5e5e5e5e-0000-4000-8000-000000000001", "orderId": "ord-1", "targetId": "tgt-1", "poleFeatureId": "pole-1", "status": 2,
                           "baselineVersion": 0, "stagedBy": "crew-1", "stagedAt": "2026-11-16T09:30:00+00:00", "closedAt": "2026-11-16T10:00:00+00:00", "changes": [] } ],
              "page": 3, "pageSize": 25, "totalCount": 51 }
            """);

        var page = await Sut(handler).GetPageAsync("ord-1", 3, 25, status, Principal, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be($"/api/workflow/service-orders/ord-1/occupancy-changes?page=3&pageSize=25&status={expectedStatus}");
        page.TotalCount.Should().Be(51);
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Status = OccupancyChangeSetStatus.Discarded, ClosedAt = (DateTimeOffset?)new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
        });
    }

    [Fact]
    public async Task GetPageAsync_AnEnvelopeWithoutItems_IsAnEmptyPage()
    {
        var page = await Sut(Answering(HttpStatusCode.OK, """{ "page": 1, "pageSize": 50, "totalCount": 0 }"""))
            .GetPageAsync("ord-1", 1, 50, null, Principal, CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task DiscardAsync_DeletesTheTargetsChanges_AndSaysWhetherAnythingWasDiscarded(int discarded, bool expected)
    {
        var handler = Answering(HttpStatusCode.OK, $$"""{ "discarded": {{discarded}} }""");

        var result = await Sut(handler).DiscardAsync("ord-1", "tgt-1", Principal, CancellationToken.None);

        result.Should().Be(expected);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/tgt-1/occupancy-changes");
    }

    [Fact]
    public async Task DiscardAllAsync_DeletesTheOrdersChanges_AndReturnsHowMany()
    {
        var handler = Answering(HttpStatusCode.OK, """{ "discarded": 7 }""");

        var result = await Sut(handler).DiscardAllAsync("ord 1", Principal, CancellationToken.None);

        result.Should().Be(7);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord%201/occupancy-changes");
    }

    // ── Guards: nothing is sent ───────────────────────────────────────────────

    [Fact]
    public async Task InvalidArguments_AreRejectedBeforeAnyRequest()
    {
        var handler = Answering(HttpStatusCode.OK, ViewJson);
        var sut = Sut(handler);
        Func<Task>[] calls =
        [
            () => sut.GetAsync(" ", "tgt-1", Principal, CancellationToken.None),
            () => sut.GetAsync("ord-1", "", Principal, CancellationToken.None),
            () => sut.GetAsync("ord-1", "tgt-1", null!, CancellationToken.None),
            () => sut.StageAsync("ord-1", "tgt-1", null!, Principal, CancellationToken.None),
            () => sut.StageAsync("ord-1", " ", [], Principal, CancellationToken.None),
            () => sut.DiscardAsync("", "tgt-1", Principal, CancellationToken.None),
            () => sut.DiscardAsync("ord-1", "tgt-1", null!, CancellationToken.None),
            () => sut.DiscardAllAsync(" ", Principal, CancellationToken.None),
            () => sut.DiscardAllAsync("ord-1", null!, CancellationToken.None),
            () => sut.GetPageAsync(" ", 1, 50, null, Principal, CancellationToken.None),
            () => sut.GetPageAsync("ord-1", 0, 50, null, Principal, CancellationToken.None),
            () => sut.GetPageAsync("ord-1", 1, 201, null, Principal, CancellationToken.None),
            () => sut.GetPageAsync("ord-1", 1, 50, (OccupancyChangeSetStatus)9, Principal, CancellationToken.None),
            () => sut.GetPageAsync("ord-1", 1, 50, null, null!, CancellationToken.None),
        ];

        foreach (var call in calls)
            await call.Should().ThrowAsync<ArgumentException>();

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AlreadyCancelledToken_ThrowsBeforeAnyRequest()
    {
        var handler = Answering(HttpStatusCode.OK, ViewJson);
        var sut = Sut(handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Func<Task>[] calls =
        [
            () => sut.GetAsync("ord-1", "tgt-1", Principal, cancelled.Token),
            () => sut.GetPageAsync("ord-1", 1, 50, null, Principal, cancelled.Token),
            () => sut.StageAsync("ord-1", "tgt-1", [], Principal, cancelled.Token),
            () => sut.DiscardAsync("ord-1", "tgt-1", Principal, cancelled.Token),
            () => sut.DiscardAllAsync("ord-1", Principal, cancelled.Token),
        ];

        foreach (var call in calls)
            await call.Should().ThrowAsync<OperationCanceledException>();

        handler.Requests.Should().BeEmpty();
    }

    // ── Problems come back as the service's own exceptions ────────────────────

    [Fact]
    public async Task InvalidChange_BecomesInvalidOccupancyChangeException_CarryingTheHostsDetail()
    {
        var act = () => Sut(Failing(HttpStatusCode.BadRequest, OccupancyProblemCodes.InvalidChange))
            .StageAsync("ord-1", "tgt-1", [], Principal, CancellationToken.None);

        (await act.Should().ThrowExactlyAsync<InvalidOccupancyChangeException>()).Which.Message.Should().Be("Detail of occupancy.invalid-change");
    }

    [Fact]
    public async Task EntryNotFound_BecomesOccupancyEntryNotFoundException_WhichIsAKeyNotFoundException()
    {
        var act = () => Sut(Failing(HttpStatusCode.NotFound, OccupancyProblemCodes.EntryNotFound, "\"slotIndex\": 5"))
            .StageAsync("ord-1", "tgt-1", [], Principal, CancellationToken.None);

        var exception = (await act.Should().ThrowExactlyAsync<OccupancyEntryNotFoundException>()).Which;
        exception.Should().BeAssignableTo<KeyNotFoundException>();
        exception.Message.Should().Be("Detail of occupancy.entry-not-found");
    }

    [Fact]
    public async Task NotAPole_BecomesTargetIsNotAPoleException_AndTargetReleased_OccupancyTargetReleasedException()
    {
        var notAPole = () => Sut(Failing(HttpStatusCode.UnprocessableEntity, OccupancyProblemCodes.NotAPole)).GetAsync("ord-1", "tgt-1", Principal, CancellationToken.None);
        var released = () => Sut(Failing(HttpStatusCode.Conflict, OccupancyProblemCodes.TargetReleased)).StageAsync("ord-1", "tgt-1", [], Principal, CancellationToken.None);

        (await notAPole.Should().ThrowExactlyAsync<TargetIsNotAPoleException>()).Which.Message.Should().Be("Detail of occupancy.not-a-pole");
        (await released.Should().ThrowExactlyAsync<OccupancyTargetReleasedException>()).Which.Message.Should().Be("Detail of occupancy.target-released");
    }

    [Fact]
    public async Task TargetNotFound_BecomesOrderTargetNotFoundException_NamingTheOrderAndTheTarget_AndOrderNotFound_APlainKeyNotFound()
    {
        var target = () => Sut(Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound)).GetAsync("ord-1", "tgt-9", Principal, CancellationToken.None);
        var order = () => Sut(Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)).GetAsync("ord-0", "tgt-9", Principal, CancellationToken.None);
        var orderOnAList = () => Sut(Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)).DiscardAllAsync("ord-0", Principal, CancellationToken.None);

        (await target.Should().ThrowExactlyAsync<OrderTargetNotFoundException>()).Which.Should().BeEquivalentTo(new { OrderId = "ord-1", TargetId = "tgt-9" });
        await order.Should().ThrowExactlyAsync<KeyNotFoundException>();
        await orderOnAList.Should().ThrowExactlyAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Denied_BecomesOrderActionDeniedException_ForTheActionTheCallNeeded()
    {
        var read = () => Sut(Failing(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"Owner only.\"")).GetAsync("ord-1", "tgt-1", Principal, CancellationToken.None);
        var list = () => Sut(Failing(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"Owner only.\"")).GetPageAsync("ord-1", 1, 50, null, Principal, CancellationToken.None);
        var write = () => Sut(Failing(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"Owner only.\"")).DiscardAsync("ord-1", "tgt-1", Principal, CancellationToken.None);

        (await read.Should().ThrowExactlyAsync<OrderActionDeniedException>()).Which.Evaluation.Should().Be(RuleEvaluationResult.Deny(OrderActionType.View, "Owner only."));
        (await list.Should().ThrowExactlyAsync<OrderActionDeniedException>()).Which.Action.Should().Be(OrderActionType.View);
        (await write.Should().ThrowExactlyAsync<OrderActionDeniedException>()).Which.Should().BeEquivalentTo(new { OrderId = "ord-1", Action = (OrderActionType?)OrderActionType.Annotate });
    }

    [Fact]
    public async Task TerminalOrder_BecomesInvalidServiceOrderTransitionException_AndPagingOutOfRange_ArgumentOutOfRange()
    {
        var closed = () => Sut(Failing(HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState, "\"from\": \"Completed\", \"to\": \"Completed\""))
            .StageAsync("ord-1", "tgt-1", [], Principal, CancellationToken.None);
        var paging = () => Sut(Failing(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange)).GetPageAsync("ord-1", 1, 50, null, Principal, CancellationToken.None);
        var validation = () => Sut(Failing(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed)).StageAsync("ord-1", "tgt-1", [], Principal, CancellationToken.None);

        (await closed.Should().ThrowExactlyAsync<InvalidServiceOrderTransitionException>()).Which.Should().BeEquivalentTo(new { From = "Completed", To = "Completed" });
        await paging.Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
        await validation.Should().ThrowExactlyAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailureThatIsNotARecognisedProblem_StaysAnHttpRequestException(HttpStatusCode status)
    {
        var act = () => Sut(new BodyRecordingHandler(_ => new HttpResponseMessage(status))).GetAsync("ord-1", "tgt-1", Principal, CancellationToken.None);

        (await act.Should().ThrowExactlyAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task EveryOccupancyProblemCodeARouteCanAnswerWith_IsTranslated_AndTheWarningOnlyCodeIsNot()
    {
        var codes = typeof(OccupancyProblemCodes).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        codes.Should().HaveCount(5);
        foreach (var code in codes)
        {
            using var response = BodyRecordingHandler.Problem(HttpStatusCode.BadRequest, code);
            var problem = await WorkflowProblemTranslator.ReadAsync(response, CancellationToken.None);
            var translated = WorkflowProblemTranslator.ToException(problem!, "ord-1", OrderActionType.View);

            if (string.Equals(code, OccupancyProblemCodes.StaleBaseline, StringComparison.Ordinal))
                translated.Should().BeNull("a stale baseline is only ever a warning inside a successful read");
            else
                translated.Should().NotBeNull("code '{0}' must map to an exception", code);
        }
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWorkflowRest_RegistersTheOccupancyClient_OnTheNamedGeoAssetsServerClient()
    {
        var handler = Answering(HttpStatusCode.OK, ViewJson);
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddWorkflowRest("http://test/api/workflow");
        await using var provider = services.BuildServiceProvider();

        var staging = provider.GetRequiredService<IOrderOccupancyStagingService>();
        await staging.GetAsync("ord-1", "tgt-1", Principal, CancellationToken.None);

        staging.Should().BeOfType<RestOrderOccupancyStagingService>().And.BeSameAs(provider.GetRequiredService<RestOrderOccupancyStagingService>());
        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/tgt-1/occupancy");
    }
}
