using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.IpBilling.Server;
using GeoAssets.Workflow.Rest;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// Unit coverage of <see cref="MunicipioProvisioningService"/> (XD01-177, on IpBilling.Server since XD01-385) over a fake
/// <see cref="IOrganizationGrantClient"/>: which kernel calls a município save or delete makes, in which order, with which key.
/// </summary>
public sealed class MunicipioProvisioningServiceTests
{
    private const string IbgeCode = "3550308";

    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000385");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000385");
    private static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000385");

    private sealed class FakeGrantClient : IOrganizationGrantClient
    {
        public List<string> Calls { get; } = [];

        public List<OrganizationGrantUpsertRequest> Upserts { get; } = [];

        public List<OrganizationGrantKey> Revokes { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Exception? FailRevokeWith { get; set; }

        public Exception? FailUpsertWith { get; set; }

        public Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
        {
            Calls.Add("upsert");
            Upserts.Add(request);
            Tokens.Add(ct);
            if (FailUpsertWith is not null) return Task.FromException<OrganizationGrantUpsertResult>(FailUpsertWith);

            var grant = new OrganizationGrantDto(
                Guid.NewGuid(), request.Key.GranteeOrganizationId, request.Key.ResourceOrganizationId, request.Key.ResourceType, request.Key.ScopeId,
                request.AllowedActions, request.RequiredRole, request.ExpiresAt, request.GrantedBy, DateTime.UnixEpoch, true);
            return Task.FromResult(new OrganizationGrantUpsertResult(grant, OrganizationGrantUpsertOutcome.Created));
        }

        public Task<OrganizationGrantRevokeResult> RevokeAsync(OrganizationGrantKey key, CancellationToken ct)
        {
            Calls.Add("revoke");
            Revokes.Add(key);
            Tokens.Add(ct);
            return FailRevokeWith is not null
                ? Task.FromException<OrganizationGrantRevokeResult>(FailRevokeWith)
                : Task.FromResult(new OrganizationGrantRevokeResult(1));
        }

        public Task<OrganizationGrantPage> ListActiveAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException("The provisioning service never lists.");
    }

    private static (MunicipioProvisioningService Service, FakeGrantClient Client) Create()
    {
        var client = new FakeGrantClient();
        return (new MunicipioProvisioningService(client), client);
    }

    [Fact]
    public async Task SyncGrantAsync_NewMunicipioWithPrefeitura_UpsertsOneScopedGrant_AndRevokesNothing()
    {
        var (service, client) = Create();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraA, previousPrefeituraOrganizationId: null, "admin@test");

        client.Calls.Should().Equal("upsert");
        var request = client.Upserts.Should().ContainSingle().Subject;
        request.Key.Should().Be(new OrganizationGrantKey(PrefeituraA, Distribuidora, ResourceType: null, ScopeId: IbgeCode));
        request.AllowedActions.Should().Equal("ip-billing:read");
        request.RequiredRole.Should().BeNull();
        request.ExpiresAt.Should().BeNull();
        request.GrantedBy.Should().Be("admin@test");
    }

    [Fact]
    public async Task SyncGrantAsync_NewMunicipioWithoutPrefeitura_CallsNothing()
    {
        var (service, client) = Create();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, prefeituraOrganizationId: null, previousPrefeituraOrganizationId: null, "admin@test");

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncGrantAsync_SamePrefeituraAsBefore_UpsertsAgain_WithoutRevoking()
    {
        var (service, client) = Create();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraA, previousPrefeituraOrganizationId: PrefeituraA, "admin@test");

        client.Calls.Should().Equal("upsert");
        client.Revokes.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncGrantAsync_RelinkedToAnotherPrefeitura_RevokesThePreviousOne_ThenUpsertsTheNew()
    {
        var (service, client) = Create();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraB, previousPrefeituraOrganizationId: PrefeituraA, "admin@test");

        client.Calls.Should().Equal("revoke", "upsert");
        client.Revokes.Should().ContainSingle().Which.Should().Be(new OrganizationGrantKey(PrefeituraA, Distribuidora, null, IbgeCode));
        client.Upserts.Should().ContainSingle().Which.Key.Should().Be(new OrganizationGrantKey(PrefeituraB, Distribuidora, null, IbgeCode));
    }

    [Fact]
    public async Task SyncGrantAsync_PrefeituraLinkCleared_OnlyRevokesThePreviousGrant()
    {
        var (service, client) = Create();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, prefeituraOrganizationId: null, previousPrefeituraOrganizationId: PrefeituraA, "admin@test");

        client.Calls.Should().Equal("revoke");
        client.Revokes.Should().ContainSingle().Which.GranteeOrganizationId.Should().Be(PrefeituraA);
    }

    [Fact]
    public async Task SyncGrantAsync_ForwardsTheCallersCancellationToken_ToEveryKernelCall()
    {
        var (service, client) = Create();
        using var source = new CancellationTokenSource();

        await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraB, PrefeituraA, "admin@test", source.Token);

        client.Tokens.Should().HaveCount(2).And.OnlyContain(t => t == source.Token);
    }

    [Fact]
    public async Task SyncGrantAsync_WhenTheRevokeFails_PropagatesTheFailure_AndNeverUpserts()
    {
        var (service, client) = Create();
        client.FailRevokeWith = new HttpRequestException("kernel down");

        var act = async () => await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraB, PrefeituraA, "admin@test");

        await act.Should().ThrowAsync<HttpRequestException>();
        client.Calls.Should().Equal("revoke");
    }

    [Fact]
    public async Task SyncGrantAsync_WhenTheUpsertFails_PropagatesTheFailure_AfterThePreviousGrantWasAlreadyRevoked()
    {
        var (service, client) = Create();
        client.FailUpsertWith = new HttpRequestException("kernel down");

        var act = async () => await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraB, PrefeituraA, "admin@test");

        await act.Should().ThrowAsync<HttpRequestException>();
        client.Calls.Should().Equal("revoke", "upsert");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SyncGrantAsync_BlankIbgeCode_Throws_BeforeAnyKernelCall(string? ibgeCode)
    {
        var (service, client) = Create();

        var act = async () => await service.SyncGrantAsync(ibgeCode!, Distribuidora, PrefeituraA, null, "admin@test");

        await act.Should().ThrowAsync<ArgumentException>();
        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SyncGrantAsync_BlankGrantedBy_Throws_BeforeAnyKernelCall(string? grantedBy)
    {
        var (service, client) = Create();

        var act = async () => await service.SyncGrantAsync(IbgeCode, Distribuidora, PrefeituraB, PrefeituraA, grantedBy!);

        await act.Should().ThrowAsync<ArgumentException>();
        client.Calls.Should().BeEmpty("a bad argument must not leave the previous grant revoked");
    }

    [Fact]
    public async Task RevokeAsync_WithAPrefeitura_RevokesTheMunicipiosGrant()
    {
        var (service, client) = Create();
        using var source = new CancellationTokenSource();

        await service.RevokeAsync(IbgeCode, Distribuidora, PrefeituraA, source.Token);

        client.Revokes.Should().ContainSingle().Which.Should().Be(new OrganizationGrantKey(PrefeituraA, Distribuidora, null, IbgeCode));
        client.Tokens.Should().ContainSingle().Which.Should().Be(source.Token);
        client.Upserts.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeAsync_WithoutAPrefeitura_CallsNothing()
    {
        var (service, client) = Create();

        await service.RevokeAsync(IbgeCode, Distribuidora, prefeituraOrganizationId: null);

        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RevokeAsync_BlankIbgeCode_Throws_EvenWithoutAPrefeitura(string? ibgeCode)
    {
        var (service, client) = Create();

        var act = async () => await service.RevokeAsync(ibgeCode!, Distribuidora, null);

        await act.Should().ThrowAsync<ArgumentException>();
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public Task RevokeAsync_WhenTheKernelFails_PropagatesTheFailure()
    {
        var (service, client) = Create();
        client.FailRevokeWith = new HttpRequestException("kernel down");

        var act = async () => await service.RevokeAsync(IbgeCode, Distribuidora, PrefeituraA);

        return act.Should().ThrowAsync<HttpRequestException>();
    }
}
