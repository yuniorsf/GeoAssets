using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>
/// Collapsible "Identidad" group (XD01-85) — hidden (with its whole subtree) from users lacking
/// <c>users:read</c>, matching today's <c>HasPermissionAsync("users:read")</c>-gated <c>ShowAdmin</c>.
/// </summary>
public sealed class IdentityGroupMenuItem : MenuGroupItem
{
    public override string Id => "identity";
    public override string LabelKey => "nav.identity";
    public override string? Icon => "🛡️";
    public override int SortOrder => 70;
    public override string? RequiredPermission => "users:read";
}
