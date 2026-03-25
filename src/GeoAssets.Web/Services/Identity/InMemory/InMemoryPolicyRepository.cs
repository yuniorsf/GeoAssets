using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryPolicyRepository(WasmIdentityStore store) : IPolicyRepository
{
    public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.Policies.FirstOrDefault(p => p.Id == id));

    public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(store.Policies.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppPolicy>>(store.Policies);

    public Task AddAsync(AppPolicy policy, CancellationToken ct = default)
    {
        store.Policies.Add(policy);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppPolicy policy, CancellationToken ct = default)
    {
        var idx = store.Policies.FindIndex(p => p.Id == policy.Id);
        if (idx >= 0) store.Policies[idx] = policy;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        store.Policies.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
