namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>Cuts the connection to the kernel while <see cref="Down"/>.</summary>
internal sealed class KernelLink(HttpMessageHandler kernel) : DelegatingHandler(kernel)
{
    public bool Down { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Down
            ? throw new HttpRequestException("Connection refused (kernel.test:80).", null, System.Net.HttpStatusCode.ServiceUnavailable)
            : base.SendAsync(request, cancellationToken);
}
