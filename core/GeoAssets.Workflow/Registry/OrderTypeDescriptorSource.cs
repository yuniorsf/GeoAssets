namespace GeoAssets.Workflow.Registry;

/// <summary>
/// A vertical's descriptors contributed in process (XD01-369): a module hosted in the kernel's process registers one, and
/// the registry upserts it at bootstrap exactly as a remote vertical's <c>PUT</c> would — so lifting the module out of the
/// process changes only the transport.
/// </summary>
public sealed record OrderTypeDescriptorSource(string VerticalId, IReadOnlyList<OrderTypeDescriptor> Descriptors);
