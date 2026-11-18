using Microsoft.AspNetCore.Http;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// A module's translation of its own exceptions into RFC 9457 problem answers (XD01-363, ADR-001 smell G). Registered in DI;
/// the host tries every mapper in registration order, then falls back to its generic answers. A mapper returns
/// <see langword="null"/> for anything outside <see cref="ProblemContext.Scope"/> it does not own.
/// </summary>
public interface IProblemDetailsMapper
{
    IResult? TryMap(Exception exception, ProblemContext context);
}
