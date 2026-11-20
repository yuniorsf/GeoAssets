namespace GeoAssets.Security.Tests;

/// <summary>What a fault-injecting kernel applier does to the next calls (XD01-383).</summary>
internal enum KernelFault
{
    /// <summary>Applies the batch, as the kernel does.</summary>
    None = 0,

    /// <summary>The kernel's catalog is unreachable: every call fails before anything is applied, until the mode changes.</summary>
    DownBeforeApply = 1,

    /// <summary>The kernel applies the batch for real and then dies before it stages its reply — once; the mode is then <see cref="None"/>.</summary>
    CrashAfterApplyOnce = 2,
}
