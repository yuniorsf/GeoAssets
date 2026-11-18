using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Route inventory (XD01-354): a stable, sorted, one-line-per-endpoint description of a host's routes — HTTP methods,
/// route pattern and authorization metadata — so the server-decoupling phases can prove behaviour parity while
/// registrations move between modules and hosts.
/// </summary>
internal static class RouteInventory
{
    public static IReadOnlyList<string> Describe(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .OfType<RouteEndpoint>()
            .Select(DescribeEndpoint)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static RouteInventoryDiff Compare(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        return new RouteInventoryDiff(
            MultisetExcept(actual, expected),
            MultisetExcept(expected, actual));
    }

    private static string DescribeEndpoint(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
            .Select(m => m.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];

        var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
        if (!pattern.StartsWith('/'))
        {
            pattern = "/" + pattern;
        }

        var tokens = AuthorizationTokens(endpoint.Metadata)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return $"{(methods.Length == 0 ? "*" : string.Join(',', methods))} {pattern} | {(tokens.Length == 0 ? "none" : string.Join(", ", tokens))}";
    }

    private static IEnumerable<string> AuthorizationTokens(EndpointMetadataCollection metadata)
    {
        if (metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            yield return "anonymous";
        }

        foreach (var authorize in metadata.GetOrderedMetadata<IAuthorizeData>())
        {
            yield return string.IsNullOrEmpty(authorize.Policy) ? "authorize" : $"policy:{authorize.Policy}";

            if (!string.IsNullOrEmpty(authorize.Roles))
            {
                yield return $"roles:{authorize.Roles}";
            }
        }

        foreach (var policy in metadata.GetOrderedMetadata<AuthorizationPolicy>())
        {
            foreach (var requirement in policy.Requirements)
            {
                yield return $"requirement:{requirement.GetType().Name}";
            }
        }
    }

    private static string[] MultisetExcept(IReadOnlyList<string> source, IReadOnlyList<string> subtract)
    {
        var remaining = subtract
            .GroupBy(line => line, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var result = new List<string>();
        foreach (var line in source)
        {
            if (remaining.TryGetValue(line, out var count) && count > 0)
            {
                remaining[line] = count - 1;
            }
            else
            {
                result.Add(line);
            }
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }
}
