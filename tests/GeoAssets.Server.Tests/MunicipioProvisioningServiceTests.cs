using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Server.IpBilling;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Unit coverage for <see cref="MunicipioProvisioningService"/> (XD01-177) against an in-memory
/// <see cref="IOrganizationGrantRepository"/> — no HTTP host needed, since the service has no
/// web-layer dependency of its own.
/// </summary>
public sealed class MunicipioProvisioningServiceTests
{
    private sealed class InMemoryOrganizationGrantRepository : IOrganizationGrantRepository
    {
        private readonly List<OrganizationGrant> _grants = [];

        public List<OrganizationGrant> Grants => _grants;

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_grants.FirstOrDefault(g => g.Id == id));

        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>(_grants);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
            Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>(_grants
                .Where(g => g.IsActive
                    && g.GranteeOrganizationId == granteeOrganizationId
                    && g.ResourceOrganizationId == resourceOrganizationId)
                .ToList());

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
            Guid granteeOrganizationId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default)
        {
            _grants.Add(grant);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
            long afterSequence, int take, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private const string IbgeCode = "3550308";

    [Fact]
    public async Task SyncGrantAsync_NewMunicipioWithPrefeitura_CreatesScopedGrant()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var prefeituraId = Guid.NewGuid();

        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);

        repo.Grants.Should().ContainSingle();
        var grant = repo.Grants[0];
        grant.GranteeOrganizationId.Should().Be(prefeituraId);
        grant.ResourceOrganizationId.Should().Be(distribuidoraId);
        grant.ResourceType.Should().BeNull();
        grant.ScopeId.Should().Be(IbgeCode);
        grant.AllowedActions.Should().ContainSingle().Which.Should().Be("ip-billing:read");
        grant.GrantedBy.Should().Be("admin@test");
        grant.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SyncGrantAsync_NewMunicipioWithoutPrefeitura_CreatesNoGrant()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);

        await service.SyncGrantAsync(IbgeCode, Guid.NewGuid(), prefeituraOrganizationId: null, previousPrefeituraOrganizationId: null, "admin@test", default);

        repo.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncGrantAsync_CalledTwiceWithSamePrefeitura_DoesNotDuplicateGrant()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var prefeituraId = Guid.NewGuid();

        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);
        // Second save with the same Prefeitura link — the município's own "previous" value is
        // now the same as its current one, exactly as a real caller would observe.
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: prefeituraId, "admin@test", default);

        repo.Grants.Should().ContainSingle("a second identical save must not create a duplicate grant");
    }

    [Fact]
    public async Task SyncGrantAsync_PrefeituraLinkChangedToDifferentOrg_RevokesPreviousAndCreatesNew()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var oldPrefeituraId = Guid.NewGuid();
        var newPrefeituraId = Guid.NewGuid();

        await service.SyncGrantAsync(IbgeCode, distribuidoraId, oldPrefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, newPrefeituraId, previousPrefeituraOrganizationId: oldPrefeituraId, "admin@test", default);

        repo.Grants.Should().HaveCount(2, "the old grant is revoked in place, not deleted — a new row is added for the new org");
        repo.Grants.Single(g => g.GranteeOrganizationId == oldPrefeituraId).IsActive.Should().BeFalse();
        repo.Grants.Single(g => g.GranteeOrganizationId == newPrefeituraId).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SyncGrantAsync_PrefeituraLinkCleared_RevokesPreviousGrant()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var prefeituraId = Guid.NewGuid();

        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraOrganizationId: null, previousPrefeituraOrganizationId: prefeituraId, "admin@test", default);

        repo.Grants.Should().ContainSingle();
        repo.Grants[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_MunicipioWithActiveGrant_SetsIsActiveFalse()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var prefeituraId = Guid.NewGuid();
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);

        await service.RevokeAsync(IbgeCode, distribuidoraId, prefeituraId, default);

        repo.Grants.Should().ContainSingle();
        repo.Grants[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_MunicipioWithoutPrefeitura_IsNoOp()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);

        await service.RevokeAsync(IbgeCode, Guid.NewGuid(), prefeituraOrganizationId: null, default);

        repo.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncGrantAsync_ReassigningSamePrefeituraAfterRevoke_CreatesNewActiveGrant()
    {
        var repo = new InMemoryOrganizationGrantRepository();
        var service = new MunicipioProvisioningService(repo, TimeProvider.System);
        var distribuidoraId = Guid.NewGuid();
        var prefeituraId = Guid.NewGuid();
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);
        await service.RevokeAsync(IbgeCode, distribuidoraId, prefeituraId, default);

        // Re-assigning the same Prefeitura after a revoke must provision a fresh active grant,
        // not treat the now-inactive row as "already provisioned".
        await service.SyncGrantAsync(IbgeCode, distribuidoraId, prefeituraId, previousPrefeituraOrganizationId: null, "admin@test", default);

        repo.Grants.Should().HaveCount(2);
        repo.Grants.Count(g => g.IsActive).Should().Be(1);
    }
}
