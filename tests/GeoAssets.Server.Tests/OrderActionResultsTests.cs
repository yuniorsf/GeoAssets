using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

public sealed class OrderActionResultsTests
{
    private static async Task<(int Status, string Body)> ExecuteAsync(OrderActionOutcome outcome)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await OrderActionResults.ToResult(outcome).ExecuteAsync(context);

        return (context.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task Applied_Returns204()
    {
        var (status, body) = await ExecuteAsync(OrderActionOutcome.Applied);

        status.Should().Be((int)HttpStatusCode.NoContent);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var (status, _) = await ExecuteAsync(OrderActionOutcome.NotFound);

        status.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConcurrentModification_Returns409()
    {
        var (status, _) = await ExecuteAsync(OrderActionOutcome.ConcurrentModification);

        status.Should().Be((int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Denied_Returns403WithTheReason()
    {
        var verdict = RuleEvaluationResult.Deny(OrderActionType.Approve, "not your order");

        var (status, body) = await ExecuteAsync(OrderActionOutcome.Denied(verdict));

        status.Should().Be((int)HttpStatusCode.Forbidden);
        JsonDocument.Parse(body).RootElement.GetProperty("reason").GetString().Should().Be("not your order");
    }

    [Fact]
    public async Task Denied_OutsideTheOrganization_Returns404()
    {
        var verdict = RuleEvaluationResult.Deny(OrderActionType.Approve, "other org", RuleDenialCode.OutsideOrganization);

        var (status, _) = await ExecuteAsync(OrderActionOutcome.Denied(verdict));

        status.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidTransition_Returns400WithFromAndTo()
    {
        var (status, body) = await ExecuteAsync(OrderActionOutcome.InvalidTransition("Draft", "Done"));

        status.Should().Be((int)HttpStatusCode.BadRequest);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("from").GetString().Should().Be("Draft");
        json.GetProperty("to").GetString().Should().Be("Done");
    }

    [Fact]
    public async Task Problem_ReturnsItsStatusWithAProblemBody()
    {
        var outcome = OrderActionOutcome.Problem(409, "some.code", "A title", "A detail", ("poleFeatureIds", new[] { "p1", "p2" }));

        var (status, body) = await ExecuteAsync(outcome);

        status.Should().Be(409);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("code").GetString().Should().Be("some.code");
        json.GetProperty("title").GetString().Should().Be("A title");
        json.GetProperty("detail").GetString().Should().Be("A detail");
        json.GetProperty("status").GetInt32().Should().Be(409);
        json.GetProperty("poleFeatureIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("p1", "p2");
    }

    [Fact]
    public void NullOutcome_Throws()
    {
        var act = () => OrderActionResults.ToResult(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
