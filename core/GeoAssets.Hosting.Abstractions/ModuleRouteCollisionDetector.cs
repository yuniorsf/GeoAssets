using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Fails fast when two modules map the same route (XD01-356): the same route template — compared case-insensitively,
/// with parameter names and constraints ignored, so <c>/orders/{id}</c> and <c>/Orders/{orderId:guid}</c> collide —
/// for at least one common HTTP method (an endpoint without method metadata matches every method). Such a pair would
/// otherwise surface only at request time as an ambiguous match. Routes inside one module are that module's business.
/// </summary>
public static class ModuleRouteCollisionDetector
{
    public static void ThrowIfCollides(IReadOnlyList<(string ModuleId, IReadOnlyList<Endpoint> Endpoints)> mappedByModule)
    {
        ArgumentNullException.ThrowIfNull(mappedByModule);

        var owners = new Dictionary<string, List<(string ModuleId, IReadOnlyList<string>? Methods)>>(StringComparer.Ordinal);
        foreach (var (moduleId, endpoints) in mappedByModule)
        {
            foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
            {
                var template = NormalizedTemplate(endpoint.RoutePattern);
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                methods = methods is { Count: > 0 } ? methods : null;

                if (!owners.TryGetValue(template, out var claims))
                {
                    claims = [];
                    owners[template] = claims;
                }

                foreach (var (otherModuleId, otherMethods) in claims)
                {
                    if (!string.Equals(otherModuleId, moduleId, StringComparison.Ordinal) && Overlap(methods, otherMethods))
                    {
                        throw new InvalidOperationException(
                            $"GeoAssets modules '{otherModuleId}' and '{moduleId}' both map route '{template}'{DescribeMethods(methods, otherMethods)}.");
                    }
                }

                claims.Add((moduleId, methods));
            }
        }
    }

    internal static string NormalizedTemplate(RoutePattern pattern)
    {
        // A part is a literal, a parameter or (between a parameter and an optional one, e.g. "{name}.{ext?}") a separator.
        var segments = pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content.ToUpperInvariant(),
            RoutePatternParameterPart parameter => parameter.IsCatchAll ? "{*}" : "{}",
            _ => ((RoutePatternSeparatorPart)part).Content,
        })));

        return "/" + string.Join('/', segments);
    }

    private static bool Overlap(IReadOnlyList<string>? methods, IReadOnlyList<string>? otherMethods) =>
        methods is null
        || otherMethods is null
        || methods.Any(m => otherMethods.Contains(m, StringComparer.OrdinalIgnoreCase));

    private static string DescribeMethods(IReadOnlyList<string>? methods, IReadOnlyList<string>? otherMethods)
    {
        if (methods is null || otherMethods is null)
        {
            return string.Empty;
        }

        return $" for {string.Join(", ", methods.Where(m => otherMethods.Contains(m, StringComparer.OrdinalIgnoreCase)).Select(m => m.ToUpperInvariant()))}";
    }
}
