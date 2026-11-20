using Microsoft.AspNetCore.Authentication;

namespace GeoAssets.Server.Tests.IpBillingBridge;

internal sealed class BridgeTokenAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>True for the stand-in kernel: tokens minted for another audience are refused.</summary>
    public bool KernelOnly { get; set; }
}
