using GeoAssets.Workflow.Registry;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// What <see cref="VerticalRegistrationService"/> publishes for its host (XD01-385): the vertical's id — the
/// <c>{verticalId}</c> of the kernel's registration routes and the <c>VerticalId</c> its
/// <c>IModulePermissionContributor</c>s are selected by — and, for a vertical that runs order types the kernel must know,
/// its order-type descriptors, read from the registration's own scope. A vertical without any (one that only publishes its
/// permissions) leaves <paramref name="OrderTypes"/> null and its descriptor set at the kernel untouched.
/// </summary>
/// <param name="VerticalId">The vertical's stable id, e.g. <c>neutral-operator</c>.</param>
/// <param name="OrderTypes">The vertical's descriptors, or null when it publishes none.</param>
public sealed record VerticalRegistrationSource(
    string VerticalId,
    Func<IServiceProvider, IReadOnlyList<OrderTypeDescriptor>>? OrderTypes = null);
