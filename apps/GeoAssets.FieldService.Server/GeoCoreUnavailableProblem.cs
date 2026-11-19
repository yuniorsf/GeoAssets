using GeoAssets.Core.Interfaces;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// The problem response this host's endpoints map <see cref="GeoCoreUnavailableException"/> to
/// (XD01-386): <c>503 Service Unavailable</c> with <c>type</c> and <c>code</c>
/// <see cref="GeoCoreUnavailableException.ProblemCode"/>, so a client can tell "the core asset
/// source is down, retry later" apart from a refused or invalid request.
/// </summary>
internal static class GeoCoreUnavailableProblem
{
    public static IResult From(GeoCoreUnavailableException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return For(exception.Message);
    }

    public static IResult For(string detail) => Results.Problem(
        detail: detail,
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "The Core GeoAssets Server is unavailable.",
        type: GeoCoreUnavailableException.ProblemCode,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = GeoCoreUnavailableException.ProblemCode,
        });
}
