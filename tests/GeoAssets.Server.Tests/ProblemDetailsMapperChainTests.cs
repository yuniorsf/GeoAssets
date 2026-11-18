using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Server.Tests;

public sealed class ProblemDetailsMapperChainTests
{
    private static readonly ProblemContext Context = new("scope", "order-1");

    private sealed class StubMapper(IResult? answer) : IProblemDetailsMapper
    {
        public int Calls { get; private set; }

        public IResult? TryMap(Exception exception, ProblemContext context)
        {
            Calls++;
            return answer;
        }
    }

    [Fact]
    public void FirstAnsweringMapperWins()
    {
        var first = new StubMapper(null);
        var second = new StubMapper(Results.StatusCode(418));
        var third = new StubMapper(Results.StatusCode(419));

        var result = new ProblemDetailsMapperChain([first, second, third]).TryMap(new InvalidOperationException(), Context);

        result.Should().BeSameAs(second.TryMap(new InvalidOperationException(), Context));
        first.Calls.Should().Be(1);
        third.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NoMapperAnswers_FallsBackToWorkflowProblemResults()
    {
        var chain = new ProblemDetailsMapperChain([new StubMapper(null)]);

        var result = chain.TryMap(new SelectionLimitExceededException(1, 1, 2), Context);

        result.Should().NotBeNull();
        var (status, code) = await ProblemResultReader.ReadAsync(result!);
        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        code.Should().Be(WorkflowProblemCodes.TargetLimitExceeded);
    }

    [Fact]
    public void NothingAnswers_ReturnsNull()
    {
        new ProblemDetailsMapperChain([new StubMapper(null)]).TryMap(new InvalidOperationException(), Context)
            .Should().BeNull();
        new ProblemDetailsMapperChain([]).TryMap(new InvalidOperationException(), Context)
            .Should().BeNull();
    }

    [Fact]
    public void Constructor_NullMappers_Throws()
    {
        var act = () => new ProblemDetailsMapperChain(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryMap_NullException_Throws()
    {
        var act = () => new ProblemDetailsMapperChain([]).TryMap(null!, Context);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryMap_NullContext_Throws()
    {
        var act = () => new ProblemDetailsMapperChain([]).TryMap(new InvalidOperationException(), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
