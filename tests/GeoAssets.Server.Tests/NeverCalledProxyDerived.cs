namespace GeoAssets.Server.Tests;

// DispatchProxy needs an unsealed base; this is the only subtype of NeverCalledProxy in the assembly.
internal sealed class NeverCalledProxyDerived : NeverCalledProxy;
