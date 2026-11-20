using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="WorkflowProblemTranslator"/>: every problem <c>code</c> of the order-target routes
/// comes back as the exception type the in-process adapter throws for the same condition, with
/// the same data; anything that is not a recognised problem stays an <see cref="HttpRequestException"/>.
/// </summary>
public sealed class WorkflowProblemTranslatorTests
{
    private static Task ThrowIfFailedAsync(HttpResponseMessage response, string? orderId = "ord-1", OrderActionType action = OrderActionType.Annotate) =>
        WorkflowProblemTranslator.ThrowIfFailedAsync(response, orderId, action, CancellationToken.None);

    private static async Task<TException> TranslatedAsync<TException>(HttpStatusCode status, string code, string extraMembers = "")
        where TException : Exception
    {
        var act = async () =>
        {
            using var response = BodyRecordingHandler.Problem(status, code, extraMembers);
            await ThrowIfFailedAsync(response);
        };
        return (await act.Should().ThrowExactlyAsync<TException>()).Which;
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Created)]
    public async Task SuccessfulResponse_DoesNotThrow(HttpStatusCode status)
    {
        using var response = new HttpResponseMessage(status);

        await ThrowIfFailedAsync(response);
        (await WorkflowProblemTranslator.ReadAsync(response, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ValidationFailed_BecomesArgumentException_CarryingTheHostsDetail()
    {
        var exception = await TranslatedAsync<ArgumentException>(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);

        exception.Message.Should().StartWith("Detail of validation.failed");
        exception.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task InvalidSelectionParameters_WithoutADefect_BecomesArgumentException()
    {
        var exception = await TranslatedAsync<ArgumentException>(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);

        exception.Message.Should().StartWith("Detail of selection.invalid-parameters");
    }

    [Theory]
    [InlineData(SelectionFenceDefect.NotClosed)]
    [InlineData(SelectionFenceDefect.SelfIntersecting)]
    [InlineData(SelectionFenceDefect.TooManyVertices)]
    [InlineData(SelectionFenceDefect.Empty)]
    public async Task InvalidSelectionParameters_WithAFenceDefect_BecomesInvalidSelectionFenceException_WithThatDefect(SelectionFenceDefect defect)
    {
        var exception = await TranslatedAsync<InvalidSelectionFenceException>(
            HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters, $"\"defect\": \"{defect}\"");

        exception.Defect.Should().Be(defect);
    }

    [Theory]
    [InlineData("\"defect\": \"NotADefect\"")]
    [InlineData("\"defect\": \"notclosed\"")]
    [InlineData("\"defect\": 3")]
    [InlineData("\"defect\": \"99\"")]
    public Task InvalidSelectionParameters_WithAnUnrecognisedDefect_FallsBackToArgumentException(string defectMember) =>
        TranslatedAsync<ArgumentException>(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters, defectMember);

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, WorkflowProblemCodes.UnknownSelectionStrategy)]
    [InlineData(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)]
    [InlineData(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound)]
    [InlineData(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound)]
    public async Task NotFoundCodes_AndUnknownStrategy_BecomeKeyNotFoundException(HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var exception = await TranslatedAsync<KeyNotFoundException>(status, code);

        exception.Message.Should().Be($"Detail of {code}");
    }

    [Fact]
    public async Task PagingOutOfRange_BecomesArgumentOutOfRangeException() =>
        (await TranslatedAsync<ArgumentOutOfRangeException>(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange))
            .Message.Should().StartWith("Detail of paging.out-of-range");

    [Fact]
    public async Task SelectionTooLarge_BecomesSelectionTooLargeException_WithTheCeilingAndTheLowerBound()
    {
        var exception = await TranslatedAsync<SelectionTooLargeException>(
            HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge, "\"ceiling\": 50000, \"matchedAtLeast\": 50001");

        exception.Should().BeEquivalentTo(new { Ceiling = 50_000, MatchedAtLeast = 50_001 });
    }

    [Fact]
    public async Task TargetLimitExceeded_BecomesSelectionLimitExceededException_WithLimitCurrentAndRequested()
    {
        var exception = await TranslatedAsync<SelectionLimitExceededException>(
            HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.TargetLimitExceeded, "\"limit\": 500, \"current\": 480, \"requested\": 35");

        exception.Should().BeEquivalentTo(new { Limit = 500, Current = 480, Requested = 35 });
    }

    [Fact]
    public async Task TargetConcurrentModification_BecomesServiceOrderConcurrencyException_ForTheOrder()
    {
        var withOrder = await TranslatedAsync<ServiceOrderConcurrencyException>(
            HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification, "\"orderId\": \"ord-from-host\"");
        var withoutOrder = await TranslatedAsync<ServiceOrderConcurrencyException>(
            HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification);

        withOrder.OrderId.Should().Be("ord-from-host");
        withoutOrder.OrderId.Should().Be("ord-1", "the client falls back to the order its call was about");
    }

    [Fact]
    public async Task OrderInvalidState_BecomesInvalidServiceOrderTransitionException_WithFromAndTo()
    {
        var exception = await TranslatedAsync<InvalidServiceOrderTransitionException>(
            HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState, "\"from\": \"Completed\", \"to\": \"Completed\"");

        exception.Should().BeEquivalentTo(new { From = "Completed", To = "Completed" });
    }

    [Fact]
    public async Task NoTargetPolicy_BecomesInvalidOperationException() =>
        (await TranslatedAsync<InvalidOperationException>(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.NoTargetPolicy))
            .Message.Should().Be("Detail of orders.no-target-policy");

    [Fact]
    public async Task Denied_BecomesOrderActionDeniedException_WithTheHostsReasonAndTheActionTheCallNeeded()
    {
        var act = async () =>
        {
            using var response = BodyRecordingHandler.Problem(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"Denied by rule 'X'.\"");
            await ThrowIfFailedAsync(response, "ord-7", OrderActionType.View);
        };

        var denied = (await act.Should().ThrowExactlyAsync<OrderActionDeniedException>()).Which;
        denied.OrderId.Should().Be("ord-7");
        denied.Evaluation.Should().Be(RuleEvaluationResult.Deny(OrderActionType.View, "Denied by rule 'X'."));
    }

    [Fact]
    public async Task EveryPublishedCode_IsTranslated()
    {
        var codes = typeof(WorkflowProblemCodes).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        codes.Should().HaveCount(14).And.Contain(WorkflowProblemCodes.TargetLedgerUnavailable);
        foreach (var code in codes)
        {
            using var response = BodyRecordingHandler.Problem(HttpStatusCode.BadRequest, code);
            var problem = await WorkflowProblemTranslator.ReadAsync(response, CancellationToken.None);

            WorkflowProblemTranslator.ToException(problem!, "ord-1", OrderActionType.View)
                .Should().NotBeNull("code '{0}' must map to an exception", code)
                .And.NotBeOfType<HttpRequestException>();
        }
    }

    public static TheoryData<HttpStatusCode, string?, string> Unrecognised() => new()
    {
        { HttpStatusCode.BadRequest, """{ "code": "something.new", "title": "?" }""", "application/problem+json" },
        { HttpStatusCode.BadRequest, """{ "title": "no code" }""", "application/problem+json" },
        { HttpStatusCode.BadRequest, """{ "code": 42 }""", "application/problem+json" },
        { HttpStatusCode.BadRequest, "\"Invalid service order.\"", "application/json" },
        { HttpStatusCode.BadRequest, "<html>not json</html>", "text/html" },
        { HttpStatusCode.Unauthorized, null, "text/plain" },
        { HttpStatusCode.Forbidden, "", "text/plain" },
        { HttpStatusCode.InternalServerError, """{ "error": "boom" }""", "application/json" },
        { HttpStatusCode.BadGateway, "   ", "text/plain" },
    };

    [Theory]
    [MemberData(nameof(Unrecognised))]
    public async Task UnknownCode_NonProblemBody_EmptyBodyOrServerError_BecomesHttpRequestException(
        HttpStatusCode status, string? body, string contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        var act = async () =>
        {
            using var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body, Encoding.UTF8, contentType);
            await ThrowIfFailedAsync(response);
        };

        (await act.Should().ThrowExactlyAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task ReadAsync_UsesTheTitleWhenThereIsNoDetail_AndExposesTheExtensionMembers()
    {
        using var response = BodyRecordingHandler.Json(
            HttpStatusCode.Conflict, """{ "code": "orders.invalid-state", "title": "Only a title", "from": "Draft", "limit": "not a number" }""");

        var problem = await WorkflowProblemTranslator.ReadAsync(response, CancellationToken.None);

        problem!.Code.Should().Be("orders.invalid-state");
        problem.Detail.Should().Be("Only a title");
        problem.GetString("from").Should().Be("Draft");
        problem.GetString("missing").Should().BeNull();
        problem.GetInt32("limit").Should().Be(0);
    }
}
