using Microsoft.Extensions.Logging;

namespace GeoAssets.IpBilling.UI.Pages;

/// <summary>
/// The IP-billing admin pages' log of an authorization check that couldn't be answered (they then fail closed). Logged
/// through <see cref="ILogger"/>, never <c>Console.Error</c>: Blazor WebAssembly shows its global error banner for
/// anything written to stderr (see <c>BannedSymbols.Browser.txt</c>).
/// </summary>
internal static partial class AuthorizationLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to resolve authorization")]
    public static partial void AuthorizationCheckFailed(ILogger logger, Exception exception);
}
