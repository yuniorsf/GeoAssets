using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server.Tests;

/// <summary>Executes an <see cref="IResult"/> against a throwaway response and reads its status and problem <c>code</c>.</summary>
internal static class ProblemResultReader
{
    public static async Task<(int Status, string? Code)> ReadAsync(IResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        string? code = null;
        if (body.Length > 0)
        {
            using var json = JsonDocument.Parse(body.ToArray());
            if (json.RootElement.TryGetProperty("code", out var codeElement))
            {
                code = codeElement.GetString();
            }
        }

        return (context.Response.StatusCode, code);
    }
}
