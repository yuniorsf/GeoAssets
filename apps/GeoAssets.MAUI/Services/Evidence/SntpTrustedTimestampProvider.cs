using System.Net.Sockets;
using GeoAssets.Shared.Services.Evidence;

namespace GeoAssets.MAUI.Services.Evidence;

/// <summary>
/// Queries a public NTP server for XD01-235's trusted-timestamp binding. Falls back to
/// <see cref="TimeProvider"/> (the local system clock) on any network failure, timeout, or
/// malformed response — this ticket's hard gate is GPS accuracy (<see cref="GpsAccuracyGate"/>),
/// not NTP reachability, so evidence capture must never block on this.
/// </summary>
internal sealed class SntpTrustedTimestampProvider(TimeProvider timeProvider) : ITrustedTimestampProvider
{
    private const string NtpServerHost = "pool.ntp.org";
    private const int NtpPort = 123;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

    public async Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(QueryTimeout);

            using var udpClient = new UdpClient();
            await udpClient.Client.ConnectAsync(NtpServerHost, NtpPort, cts.Token).ConfigureAwait(false);

            var request = new byte[48];
            request[0] = 0x1B; // LI = 0 (no warning), VN = 3, Mode = 3 (client)
            await udpClient.Client.SendAsync(request, SocketFlags.None, cts.Token).ConfigureAwait(false);

            var response = new byte[48];
            var receivedBytes = await udpClient.Client.ReceiveAsync(response, SocketFlags.None, cts.Token).ConfigureAwait(false);
            if (receivedBytes < 48)
                return timeProvider.GetUtcNow();

            return NtpPacketParser.ParseTransmitTimestamp(response);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return timeProvider.GetUtcNow();
        }
    }
}
