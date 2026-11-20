using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Workflow.Registry;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// How an out-of-process vertical publishes itself to the kernel at bootstrap (XD01-382, ADR-001 Part 1.2): its order-type
/// and feature-action descriptors to the order-type registry (XD01-369) and its permissions and role bundles to the
/// identity service (XD01-377). Both are idempotent upserts made as the vertical's <c>GeoAssets.ServiceCaller</c> service
/// principal; a failure throws <see cref="HttpRequestException"/> (with the status when the kernel answered).
/// </summary>
public interface IVerticalRegistrationClient
{
    /// <summary><c>PUT /api/registry/verticals/{verticalId}/order-types</c>: replaces the vertical's whole descriptor set.</summary>
    Task PublishOrderTypesAsync(string verticalId, IReadOnlyList<OrderTypeDescriptor> descriptors, CancellationToken ct);

    /// <summary><c>PUT /api/identity/verticals/{verticalId}/permissions</c>: upserts the vertical's permissions and role bundles.</summary>
    Task PublishPermissionsAsync(string verticalId, ModulePermissionSet permissions, CancellationToken ct);
}
