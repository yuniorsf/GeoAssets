using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

public sealed class OccupancyProblemMapperTests
{
    private static readonly ProblemContext Context = new(OccupancyProblemMapper.Scope, "order-1", "target-1");

    private static ArgumentOutOfRangeException OutOfRange(int page) => new(nameof(page));

    private static ArgumentException Invalid(string value) => new("The value is invalid.", nameof(value));

    public static TheoryData<Exception, int, string> Mapped() => new()
    {
        { new OrderActionDeniedException("nope"), StatusCodes.Status403Forbidden, WorkflowProblemCodes.Denied },
        { new OrderActionDeniedException("order-1", RuleEvaluationResult.Deny(OrderActionType.Approve, "outside", RuleDenialCode.OutsideOrganization)), StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderNotFound },
        { new OrderTargetNotFoundException("order-1", "target-1"), StatusCodes.Status404NotFound, WorkflowProblemCodes.TargetNotFound },
        { new OccupancyEntryNotFoundException("pole-1", 2), StatusCodes.Status404NotFound, OccupancyProblemCodes.EntryNotFound },
        { new KeyNotFoundException(), StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderNotFound },
        { new TargetIsNotAPoleException("target-1", "type-1"), StatusCodes.Status422UnprocessableEntity, OccupancyProblemCodes.NotAPole },
        { new InvalidServiceOrderTransitionException("Draft", "Closed"), StatusCodes.Status409Conflict, WorkflowProblemCodes.OrderInvalidState },
        { new OccupancyTargetReleasedException("order-1", "target-1"), StatusCodes.Status409Conflict, OccupancyProblemCodes.TargetReleased },
        { new InvalidOccupancyChangeException("bad"), StatusCodes.Status400BadRequest, OccupancyProblemCodes.InvalidChange },
        { OutOfRange(1), StatusCodes.Status400BadRequest, WorkflowProblemCodes.PagingOutOfRange },
        { Invalid("bad"), StatusCodes.Status400BadRequest, WorkflowProblemCodes.ValidationFailed },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public async Task TryMap_OwnScope_AnswersWithStatusAndCode(Exception exception, int status, string code)
    {
        var result = new OccupancyProblemMapper().TryMap(exception, Context);

        result.Should().NotBeNull();
        (await ProblemResultReader.ReadAsync(result!)).Should().Be((status, code));
    }

    [Fact]
    public void OtherScope_ReturnsNull()
    {
        new OccupancyProblemMapper().TryMap(new KeyNotFoundException(), new ProblemContext("shared-use", "order-1"))
            .Should().BeNull();
    }

    [Fact]
    public void TryMap_OwnScope_UnhandledException_ReturnsNull()
    {
        new OccupancyProblemMapper().TryMap(new InvalidOperationException(), Context).Should().BeNull();
    }

    [Fact]
    public void TryMap_NullArguments_Throw()
    {
        var mapper = new OccupancyProblemMapper();

        ((Action)(() => mapper.TryMap(null!, Context))).Should().Throw<ArgumentNullException>();
        ((Action)(() => mapper.TryMap(new KeyNotFoundException(), null!))).Should().Throw<ArgumentNullException>();
    }
}
