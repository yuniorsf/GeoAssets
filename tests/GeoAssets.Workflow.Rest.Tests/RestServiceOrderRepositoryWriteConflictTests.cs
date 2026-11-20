using System.Net;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>XD01-388: what a <c>409 Conflict</c> on a service-order write becomes — the mappers' write-conflict translation or the order's own concurrency exception.</summary>
public sealed class RestServiceOrderRepositoryWriteConflictTests
{
    private static readonly OrderActionLog Approve = new(OrderActionType.Approve, "approver-1", new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));

    private static RestServiceOrderRepository Sut(HttpResponseMessage conflict, params IWorkflowProblemMapper[] mappers) =>
        new(new HttpClient(new FakeHttpMessageHandler(_ => conflict)) { BaseAddress = new Uri("http://test/") }, mappers);

    private static HttpResponseMessage Conflict(string code, string extraMembers = "") =>
        BodyRecordingHandler.Problem(HttpStatusCode.Conflict, code, extraMembers);

    [Fact]
    public async Task AppendActionAsync_AMappersWriteConflictException_Wins()
    {
        var mapper = new FakeProblemMapper(toWriteConflict: _ => new MappedByException("write-conflict"));

        var act = () => Sut(Conflict("vertical.conflict"), mapper).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<MappedByException>()).Which.By.Should().Be("write-conflict");
    }

    [Fact]
    public async Task AppendActionAsync_TheFirstMapperThatNamesAWriteConflict_Wins()
    {
        var declines = new FakeProblemMapper();
        var first = new FakeProblemMapper(toWriteConflict: _ => new MappedByException("first"));
        var second = new FakeProblemMapper(toWriteConflict: _ => new MappedByException("second"));

        var act = () => Sut(Conflict("vertical.conflict"), declines, first, second).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<MappedByException>()).Which.By.Should().Be("first");
    }

    [Fact]
    public async Task AppendActionAsync_ACodeMappedOnlyThroughToException_IsStillTheConcurrencyException()
    {
        var mapper = new FakeProblemMapper(toException: _ => new MappedByException("to-exception"));

        var act = () => Sut(Conflict("vertical.conflict"), mapper).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
        mapper.Asked.Should().BeEmpty("a write's conflict never consults ToException");
    }

    [Fact]
    public async Task AppendActionAsync_WithNoMappers_IsTheConcurrencyException()
    {
        var act = () => Sut(Conflict(OccupancyProblemCodes.ConcurrentModification, "\"poleFeatureIds\": [\"p-1\"]")).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
    }

    [Fact]
    public Task AppendActionAsync_AConflictWithNoProblemBody_IsTheConcurrencyException_WhateverTheMappers()
    {
        var mapper = new FakeProblemMapper(toWriteConflict: _ => new MappedByException("never"));

        var act = () => Sut(new HttpResponseMessage(HttpStatusCode.Conflict), mapper).AppendActionAsync("ord-1", Approve);

        return act.Should().ThrowExactlyAsync<ServiceOrderConcurrencyException>();
    }

    [Fact]
    public async Task AppendActionAsync_ApplyPending_WithTheNeutralOperatorMapper_IsStillTheConcurrencyException()
    {
        var act = () => Sut(Conflict(OccupancyProblemCodes.ApplyPending, "\"poleFeatureIds\": [\"p-1\"]"), new NeutralOperatorProblemMapper())
            .AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
    }

    [Fact]
    public async Task AppendActionAsync_OccupancyConcurrentModification_WithTheNeutralOperatorMapper_NamesThePoles()
    {
        var act = () => Sut(Conflict(OccupancyProblemCodes.ConcurrentModification, "\"poleFeatureIds\": [\"pole-1\", \"pole-3\"]"), new NeutralOperatorProblemMapper())
            .AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowExactlyAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1", "pole-3");
    }
}
