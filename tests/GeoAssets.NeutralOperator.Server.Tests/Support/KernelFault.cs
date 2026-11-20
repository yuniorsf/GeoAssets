namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>How the stubbed kernel's ledger misbehaves on one operation.</summary>
internal enum KernelFault
{
    None,

    /// <summary>409 <c>targets.conflict</c> (reserve only).</summary>
    Conflict,

    /// <summary>The kernel answers 503.</summary>
    ServerError,

    /// <summary>The connection fails.</summary>
    Unreachable,
}
