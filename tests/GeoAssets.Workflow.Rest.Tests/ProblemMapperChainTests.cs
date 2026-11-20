using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>XD01-388: the verticals' <see cref="IWorkflowProblemMapper"/>s in front of the generic problem codes.</summary>
public sealed class ProblemMapperChainTests
{
    private const string VerticalCode = "vertical.thing";

    private static readonly WorkflowPrincipal Principal = new("u", "o", ["Administrator"], [], ["serviceorders:annotate"]);

    private static WorkflowProblem Problem(string code) => FakeProblemMapper.Problem(code);

    [Fact]
    public void ToException_AMapperIsAskedFirst_AndItsExceptionWinsOverTheGenericCode()
    {
        var mapper = new FakeProblemMapper(_ => new MappedByException("mapper"));

        var translated = WorkflowProblemTranslator.ToException(Problem(WorkflowProblemCodes.OrderNotFound), "o-1", OrderAction.View, [mapper]);

        translated.Should().BeOfType<MappedByException>();
        mapper.Asked.Should().Equal(WorkflowProblemCodes.OrderNotFound);
    }

    [Fact]
    public void ToException_TheFirstNonNullMapperWins_AndLaterOnesAreNotAsked()
    {
        var declines = new FakeProblemMapper();
        var first = new FakeProblemMapper(_ => new MappedByException("first"));
        var second = new FakeProblemMapper(_ => new MappedByException("second"));

        var translated = WorkflowProblemTranslator.ToException(Problem(VerticalCode), null, OrderAction.View, [declines, first, second]);

        translated.Should().BeOfType<MappedByException>().Which.By.Should().Be("first");
        declines.Asked.Should().ContainSingle();
        second.Asked.Should().BeEmpty();
    }

    [Fact]
    public void ToException_WhenEveryMapperDeclines_FallsBackToTheGenericCodes()
    {
        var mapper = new FakeProblemMapper();

        var translated = WorkflowProblemTranslator.ToException(Problem(WorkflowProblemCodes.OrderNotFound), "o-1", OrderAction.View, [mapper]);

        translated.Should().BeOfType<KeyNotFoundException>();
        mapper.Asked.Should().ContainSingle();
    }

    [Fact]
    public void ToException_ACodeNoMapperAndNoGenericCodeKnows_IsNull()
    {
        WorkflowProblemTranslator.ToException(Problem(VerticalCode), "o-1", OrderAction.View, [new FakeProblemMapper()]).Should().BeNull();
        WorkflowProblemTranslator.ToException(Problem(VerticalCode), "o-1", OrderAction.View, []).Should().BeNull();
    }

    [Theory]
    [InlineData("occupancy.invalid-change")]
    [InlineData("occupancy.concurrent-modification")]
    [InlineData("occupancy.apply-pending")]
    [InlineData("dispatch.no-targets")]
    [InlineData("orders.no-fan-out")]
    public void ToException_TheGenericOverload_KnowsNoVerticalCode(string code)
    {
        WorkflowProblemTranslator.ToException(Problem(code), "o-1", OrderAction.View).Should().BeNull();
    }

    private static async Task<Exception?> CatchAsync(
        HttpStatusCode status, string code, params IWorkflowProblemMapper[] mappers)
    {
        using var response = BodyRecordingHandler.Problem(status, code);
        try
        {
            await WorkflowProblemTranslator.ThrowIfFailedAsync(response, "o-1", OrderAction.View, mappers, CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task ThrowIfFailedAsync_AMappedProblem_ThrowsTheMappersException()
    {
        var mapper = new FakeProblemMapper(p => string.Equals(p.Code, VerticalCode, StringComparison.Ordinal) ? new MappedByException("mapper") : null);

        var thrown = await CatchAsync(HttpStatusCode.UnprocessableEntity, VerticalCode, mapper);

        thrown.Should().BeOfType<MappedByException>();
    }

    [Fact]
    public async Task ThrowIfFailedAsync_AnUnknownCode_IsAnHttpRequestException_WithOrWithoutMappers()
    {
        var withMappers = await CatchAsync(HttpStatusCode.UnprocessableEntity, VerticalCode, new FakeProblemMapper());
        var without = await CatchAsync(HttpStatusCode.UnprocessableEntity, VerticalCode);

        withMappers.Should().BeOfType<HttpRequestException>().Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        without.Should().BeOfType<HttpRequestException>().Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ThrowIfFailedAsync_TheGenericOverload_IgnoresAVerticalCode()
    {
        using var response = BodyRecordingHandler.Problem(HttpStatusCode.Conflict, "occupancy.apply-pending");
        Exception? thrown = null;
        try
        {
            await WorkflowProblemTranslator.ThrowIfFailedAsync(response, "o-1", OrderAction.Approve, CancellationToken.None);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task ThrowIfFailedAsync_ASuccessfulResponse_ReturnsWithoutAskingAMapper()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var mapper = new FakeProblemMapper();

        await WorkflowProblemTranslator.ThrowIfFailedAsync(response, "o-1", OrderAction.View, [mapper], CancellationToken.None);

        mapper.Asked.Should().BeEmpty();
    }

    [Fact]
    public void ToException_NullArguments_Throw()
    {
        var withNullMappers = () => WorkflowProblemTranslator.ToException(Problem(VerticalCode), null, OrderAction.View, null!);
        var withNullProblem = () => WorkflowProblemTranslator.ToException(null!, null, OrderAction.View, []);

        withNullMappers.Should().Throw<ArgumentNullException>();
        withNullProblem.Should().Throw<ArgumentNullException>();
    }

    // ── Lazy resolution: a mapper registered after AddWorkflowRest / AddDescriptorRoutedWorkflowRest still reaches the clients ──

    private static ServiceProvider Build(BodyRecordingHandler handler, bool routed, FakeProblemMapper mapper)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        if (routed)
            services.AddDescriptorRoutedWorkflowRest("http://test/api/workflow");
        else
            services.AddWorkflowRest("http://test/api/workflow");

        // After the chassis registration: the clients must resolve the mappers when first built, not when registered.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowProblemMapper>(mapper));
        return services.BuildServiceProvider();
    }

    private static BodyRecordingHandler Answering() => new(request =>
        request.PathAndQuery.StartsWith("/api/registry/", StringComparison.Ordinal)
            ? BodyRecordingHandler.Json(HttpStatusCode.OK, "[]")
            : BodyRecordingHandler.Problem(HttpStatusCode.UnprocessableEntity, VerticalCode));

    private static FakeProblemMapper Mapper() =>
        new(p => string.Equals(p.Code, VerticalCode, StringComparison.Ordinal) ? new MappedByException("late") : null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARepositoryOfTheTargets_UsesAMapperRegisteredAfterTheChassis(bool routed)
    {
        await using var provider = Build(Answering(), routed, Mapper());

        var act = () => provider.GetRequiredService<IServiceOrderTargetReader>().CountAsync("ord-1", CancellationToken.None);

        await act.Should().ThrowExactlyAsync<MappedByException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSelectionService_UsesAMapperRegisteredAfterTheChassis(bool routed)
    {
        await using var provider = Build(Answering(), routed, Mapper());
        var request = new TargetSelectionRequest("ord-1", "polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal), page: 1, pageSize: 10, acceptTruncation: false);

        var act = () => provider.GetRequiredService<IOrderTargetSelectionService>().PreviewAsync(request, Principal, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<MappedByException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheMapSelectionClient_UsesAMapperRegisteredAfterTheChassis(bool routed)
    {
        await using var provider = Build(Answering(), routed, Mapper());

        var act = () => provider.GetRequiredService<RestMapSelectionClient>()
            .ResolveAsync("polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<MappedByException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheOrderRepository_UsesAMapperRegisteredAfterTheChassis_ForAWriteConflict(bool routed)
    {
        var handler = new BodyRecordingHandler(request =>
            request.PathAndQuery.StartsWith("/api/registry/", StringComparison.Ordinal)
                ? BodyRecordingHandler.Json(HttpStatusCode.OK, "[]")
                : BodyRecordingHandler.Problem(HttpStatusCode.Conflict, VerticalCode));
        var mapper = new FakeProblemMapper(toWriteConflict: p => string.Equals(p.Code, VerticalCode, StringComparison.Ordinal) ? new MappedByException("late") : null);
        await using var provider = Build(handler, routed, mapper);
        var entry = new OrderActionLog(OrderActionType.Approve, "approver", new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));

        var act = () => provider.GetRequiredService<IServiceOrderWriter>().AppendActionAsync("ord-1", entry);

        await act.Should().ThrowExactlyAsync<MappedByException>();
    }

    [Fact]
    public void TheChassisRegistrations_RegisterNoMapperOfTheirOwn()
    {
        var plain = new ServiceCollection().AddWorkflowRest("http://test/api/workflow");
        var routed = new ServiceCollection().AddDescriptorRoutedWorkflowRest("http://test/api/workflow");

        plain.Should().NotContain(d => d.ServiceType == typeof(IWorkflowProblemMapper));
        routed.Should().NotContain(d => d.ServiceType == typeof(IWorkflowProblemMapper));
    }
}
