using System.Net;
using FluentAssertions;
using GeoAssets.IpBilling.Server;
using Xunit;
using static GeoAssets.Server.Tests.IpBillingBridge.MunicipioGrantSyncHost;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-395: saving a município syncs its Prefeitura grant — the real <c>GeoAssets.IpBilling.Server</c> host (município
/// endpoints, outbox on SQLite, <see cref="MunicipioGrantSyncDispatcher"/>, <c>RestOrganizationGrantClient</c>) against the
/// kernel's own grant endpoints (<see cref="StubKernel"/>). The dispatcher runs one cycle at a time under a fake clock.
/// </summary>
public sealed class MunicipioGrantSyncEndToEndTests
{
    [Fact]
    public async Task Create_WithAPrefeitura_ReportsPending_ThenTheDispatcherCreatesTheGrantAndItIsSynced()
    {
        await using var host = await StartAsync();

        var (status, _, state) = await host.CreateAsync(PrefeituraA);

        status.Should().Be(HttpStatusCode.Created);
        state.Should().Be("pending");
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty("nothing reaches the kernel inside the save");
        (await host.StatusAsync()).State.Should().Be("pending");

        (await host.DrainAsync()).Should().Be(1);

        var grant = (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle().Subject;
        grant.ScopeId.Should().Be(IbgeCode);
        grant.ResourceType.Should().BeNull();
        grant.AllowedActions.Should().Equal("ip-billing:read");
        var (syncedState, error, syncedAt) = await host.StatusAsync();
        syncedState.Should().Be("synced");
        error.Should().BeNull();
        syncedAt.Should().Be(host.Time.GetUtcNow());
    }

    [Fact]
    public async Task Create_WithoutAPrefeitura_QueuesNothing_AndTheKernelIsNeverCalled()
    {
        await using var host = await StartAsync();

        var (status, _, state) = await host.CreateAsync(prefeitura: null);

        status.Should().Be(HttpStatusCode.Created);
        state.Should().Be("none");
        (await host.DispatchAsync()).Should().Be(0);
        (await host.RequestCountAsync()).Should().Be(0);
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
        (await host.StatusAsync()).State.Should().Be("none");
    }

    [Fact]
    public async Task Update_ChangingThePrefeitura_RevokesTheOldGrant_AndGrantsTheNew()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();

        (await host.UpdateAsync(id, PrefeituraB)).Should().Be(HttpStatusCode.NoContent);
        (await host.StatusAsync()).State.Should().Be("pending");
        await host.DrainAsync();

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraB, Distribuidora)).Should().ContainSingle().Which.ScopeId.Should().Be(IbgeCode);
        (await host.StatusAsync()).State.Should().Be("synced");
    }

    [Fact]
    public async Task Update_ClearingThePrefeitura_RevokesTheGrant_AndCreatesNone()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();

        (await host.UpdateAsync(id, prefeitura: null)).Should().Be(HttpStatusCode.NoContent);
        await host.DrainAsync();

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        host.Kernel.To(HttpMethod.Put, "/api/identity/organization-grants").Should().ContainSingle("only the create upserted");
        (await host.StatusAsync()).State.Should().Be("synced");
    }

    [Fact]
    public async Task Delete_AMunicipioWithAPrefeitura_RevokesItsGrant()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();

        (await host.DeleteAsync(id)).Should().Be(HttpStatusCode.NoContent);
        await host.DrainAsync();

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.RequestCountAsync()).Should().Be(2, "the revoke outlives the município row");
    }

    [Fact]
    public async Task Delete_AMunicipioWithoutAPrefeitura_QueuesNothing()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(prefeitura: null);

        (await host.DeleteAsync(id)).Should().Be(HttpStatusCode.NoContent);

        (await host.RequestCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delete_AnUnknownMunicipio_StillAnswers204_AndQueuesNothing()
    {
        await using var host = await StartAsync();

        (await host.DeleteAsync(Guid.NewGuid())).Should().Be(HttpStatusCode.NoContent);

        (await host.RequestCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_ChangingTheIbgeCodeUnderTheSamePrefeitura_RevokesTheOldKey_AndGrantsTheNew()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();

        (await host.UpdateAsync(id, PrefeituraA, ibgeCode: "3304557")).Should().Be(HttpStatusCode.NoContent);
        await host.DrainAsync();

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle().Which.ScopeId.Should().Be("3304557");
    }

    [Fact]
    public async Task Update_ChangingTheDistribuidoraUnderTheSamePrefeitura_RevokesTheOldKey_AndGrantsTheNew()
    {
        await using var host = await StartAsync();
        var newDistribuidora = Guid.Parse("d1d1d1d1-0000-4000-8000-000000000395");
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();

        (await host.UpdateAsync(id, PrefeituraA, distribuidora: newDistribuidora)).Should().Be(HttpStatusCode.NoContent);
        await host.DrainAsync();

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, newDistribuidora)).Should().ContainSingle().Which.ScopeId.Should().Be(IbgeCode);
    }

    [Fact]
    public async Task Update_NothingGrantRelevant_QueuesNothing()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();
        var kernelCalls = host.Kernel.Requests.Count;

        (await host.UpdateAsync(id, PrefeituraA, name: "São Paulo (renamed)")).Should().Be(HttpStatusCode.NoContent);

        (await host.RequestCountAsync()).Should().Be(1);
        (await host.DispatchAsync()).Should().Be(0);
        host.Kernel.Requests.Should().HaveCount(kernelCalls);
        (await host.StatusAsync()).State.Should().Be("synced", "the latest request is still the completed one");
    }

    [Fact]
    public async Task Save_WhenTheKernelIsDown_Succeeds_StaysPending_AndConvergesOnceTheKernelIsBack()
    {
        await using var host = await StartAsync();
        host.Host.Link.Down = true;

        var (status, _, state) = await host.CreateAsync(PrefeituraA);

        status.Should().Be(HttpStatusCode.Created, "a kernel outage never fails the save");
        state.Should().Be("pending");

        (await host.DispatchAsync()).Should().Be(1);
        var (failedState, error, _) = await host.StatusAsync();
        failedState.Should().Be("pending");
        error.Should().NotBeNullOrWhiteSpace();
        host.Host.Unhandled.Should().BeEmpty();

        host.Host.Link.Down = false;
        (await host.DispatchAsync()).Should().Be(0, "the retry is scheduled after the backoff");
        host.Time.Advance(TimeSpan.FromSeconds(10));
        (await host.DispatchAsync()).Should().Be(1);

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        var (finalState, finalError, syncedAt) = await host.StatusAsync();
        finalState.Should().Be("synced");
        finalError.Should().BeNull();
        syncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Redelivery_OfACompletedRequest_IsANoOpOnTheKernel()
    {
        await using var host = await StartAsync();
        await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();
        var outbox = (await host.Kernel.OutboxAsync()).Count;

        // A crash between the kernel call and the completion write: the request is unfinished again.
        await host.ExecuteSqlAsync("UPDATE \"MunicipioGrantSyncRequests\" SET \"CompletedAt\" = NULL");
        (await host.DispatchAsync()).Should().Be(1);

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        (await host.Kernel.OutboxAsync()).Should().HaveCount(outbox, "a matching grant is left as it is");
        (await host.StatusAsync()).State.Should().Be("synced");
    }

    [Fact]
    public async Task Redelivery_OfARevoke_IsANoOpOnTheKernel()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.DrainAsync();
        await host.DeleteAsync(id);
        await host.DrainAsync();
        var outbox = (await host.Kernel.OutboxAsync()).Count;

        await host.ExecuteSqlAsync("UPDATE \"MunicipioGrantSyncRequests\" SET \"CompletedAt\" = NULL WHERE \"Kind\" = 'Revoke'");
        (await host.DispatchAsync()).Should().Be(1);

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.Kernel.OutboxAsync()).Should().HaveCount(outbox);
    }

    [Fact]
    public async Task ACrashedWorkersLeasedRequest_IsRetriedOnceItsLeaseExpires()
    {
        await using var host = await StartAsync();
        await host.CreateAsync(PrefeituraA);
        (await host.ClaimAsAnotherWorkerAsync("crashed-worker", TimeSpan.FromMinutes(2))).Should().Be(1);

        (await host.DispatchAsync()).Should().Be(0, "the lease is still held");
        host.Time.Advance(TimeSpan.FromMinutes(3));
        (await host.DispatchAsync()).Should().Be(1);

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        (await host.StatusAsync()).State.Should().Be("synced");
    }

    [Fact]
    public async Task TwoSavesOfOneMunicipio_AreSentInOrder_OneCyclePerRequest()
    {
        await using var host = await StartAsync();
        var (_, id, _) = await host.CreateAsync(PrefeituraA);
        await host.UpdateAsync(id, PrefeituraB);

        (await host.DispatchAsync()).Should().Be(1, "only the oldest unfinished request of a município is claimed");
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraB, Distribuidora)).Should().BeEmpty();
        (await host.DispatchAsync()).Should().Be(1);

        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraB, Distribuidora)).Should().ContainSingle();
    }

    [Fact]
    public async Task TheRequester_IsTheTokensUserId_NotASpoofedHeader()
    {
        await using var host = await StartAsync();

        await host.CreateAsync(PrefeituraA, configure: request =>
        {
            request.Headers.Add("X-User-Id", "spoofed-user");
            request.Headers.Add("X-Requested-By", "spoofed-user");
            request.Headers.Add("X-GeoAssets-User", "spoofed-user");
        });
        await host.DrainAsync();

        (await host.RequestedByOfFirstRequestAsync()).Should().Be("reference-manager");
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle().Which.GrantedBy.Should().Be("reference-manager");
    }
}
