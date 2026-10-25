namespace GeoAssets.Server.Tests;

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var snapshot = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            snapshot.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var copy = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            snapshot.Content = copy;
        }

        Requests.Add(snapshot);
        return respond(snapshot);
    }
}
