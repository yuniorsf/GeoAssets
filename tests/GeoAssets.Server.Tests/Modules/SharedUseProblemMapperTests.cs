using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

public sealed class SharedUseProblemMapperTests
{
    private static readonly ProblemContext Context = new(SharedUseProblemMapper.Scope, "order-1");

    private static ArgumentOutOfRangeException OutOfRange(int page) => new(nameof(page));

    private static ArgumentException Invalid(string value) => new("The value is invalid.", nameof(value));

    public static TheoryData<Exception, int, string> Mapped() => new()
    {
        { new OrderActionDeniedException("nope"), StatusCodes.Status403Forbidden, WorkflowProblemCodes.Denied },
        { new OrderActionDeniedException("order-1", RuleEvaluationResult.Deny(OrderActionType.Dispatch, "outside", RuleDenialCode.OutsideOrganization)), StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderNotFound },
        { new OrderTypeNotRegisteredException("type-1", "unknown"), StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderTypeNotFound },
        { new KeyNotFoundException(), StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderNotFound },
        { new OrderTypeHasNoFanOutException("type-1", "no fan-out"), StatusCodes.Status422UnprocessableEntity, SharedUseProblemCodes.NoFanOut },
        { new SharedUseDispatchBlockedException("order-1", SharedUseDispatchBlockReason.NoTargets), StatusCodes.Status422UnprocessableEntity, SharedUseProblemCodes.NoTargets },
        { new SharedUseDispatchBlockedException("order-1", SharedUseDispatchBlockReason.AllTargetsBlocked, ["t1"]), StatusCodes.Status422UnprocessableEntity, SharedUseProblemCodes.AllTargetsBlocked },
        { new InvalidServiceOrderTransitionException("Draft", "Closed"), StatusCodes.Status409Conflict, WorkflowProblemCodes.OrderInvalidState },
        { new ServiceOrderConcurrencyException("order-1"), StatusCodes.Status409Conflict, WorkflowProblemCodes.TargetConcurrentModification },
        { OutOfRange(1), StatusCodes.Status400BadRequest, WorkflowProblemCodes.PagingOutOfRange },
        { Invalid("bad"), StatusCodes.Status400BadRequest, WorkflowProblemCodes.ValidationFailed },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public async Task TryMap_OwnScope_AnswersWithStatusAndCode(Exception exception, int status, string code)
    {
        var result = new SharedUseProblemMapper().TryMap(exception, Context);

        result.Should().NotBeNull();
        (await ProblemResultReader.ReadAsync(result!)).Should().Be((status, code));
    }

    [Fact]
    public void OtherScope_ReturnsNull()
    {
        new SharedUseProblemMapper().TryMap(new KeyNotFoundException(), new ProblemContext("occupancy", "order-1"))
            .Should().BeNull();
    }

    [Fact]
    public void TryMap_OwnScope_UnhandledException_ReturnsNull()
    {
        new SharedUseProblemMapper().TryMap(new InvalidOperationException(), Context).Should().BeNull();
    }

    [Fact]
    public void TryMap_NullArguments_Throw()
    {
        var mapper = new SharedUseProblemMapper();

        ((Action)(() => mapper.TryMap(null!, Context))).Should().Throw<ArgumentNullException>();
        ((Action)(() => mapper.TryMap(new KeyNotFoundException(), null!))).Should().Throw<ArgumentNullException>();
    }
}
