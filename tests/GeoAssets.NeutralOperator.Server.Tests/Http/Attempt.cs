using System.Net;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// What a request to the host came to. The host has no exception-handling middleware, so a failure no route translates
/// into a problem surfaces through the in-process test server as the exception itself, and through a real server as a 500.
/// </summary>
internal sealed record Attempt(HttpStatusCode? Status, Exception? Failure)
{
    public bool Succeeded => Status is { } status && (int)status < 400;

    public static async Task<Attempt> OfAsync(Func<Task<HttpResponseMessage>> request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await request();
            return new Attempt(response.StatusCode, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Attempt(null, ex);
        }
    }
}
