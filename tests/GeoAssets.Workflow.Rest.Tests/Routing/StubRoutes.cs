using GeoAssets.Workflow.Rest.Routing;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>A fixed <see cref="IOrderApiRoutes"/>: the kernel's root, the vertical's, and the order types the vertical's root serves.</summary>
internal sealed class StubRoutes(IReadOnlyList<Uri> roots, IReadOnlyDictionary<string, Uri>? byOrderType = null) : IOrderApiRoutes
{
    public int RootReads { get; private set; }

    public Uri DefaultRoot => roots[0];

    public Task<IReadOnlyList<Uri>> GetRootsAsync(CancellationToken ct)
    {
        RootReads++;
        return Task.FromResult(roots);
    }

    public Task<Uri> ResolveAsync(string orderTypeId, CancellationToken ct) =>
        Task.FromResult(byOrderType is not null && byOrderType.TryGetValue(orderTypeId, out var root) ? root : DefaultRoot);

    public static StubRoutes KernelOnly() => new([GatewayDouble.Kernel]);

    public static StubRoutes WithVertical(params string[] verticalOrderTypes) =>
        new([GatewayDouble.Kernel, GatewayDouble.Vertical], verticalOrderTypes.ToDictionary(t => t, _ => GatewayDouble.Vertical, StringComparer.Ordinal));
}
