using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// Who a cached tile was rendered for (ADR-002 D6, XD01-431) — the <c>(orgScope, grantScopeHash)</c> half of a
/// <see cref="TileCacheKey"/>: a tile source answers in the caller's tenant scope (its organization's features plus those
/// other organizations granted it), so two callers share a cached tile only when both their
/// <see cref="OrganizationScope"/> and their <see cref="GrantScopeHash"/> are equal. Both follow
/// <see cref="TileKeySegment"/>. <c>default</c> is not a scope: a <see cref="TileCacheKey"/> refuses it.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileCacheScope
{
    /// <summary>The scope of <paramref name="organizationScope"/> (e.g. the organization id) with the grants hashed as <paramref name="grantScopeHash"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An argument breaks <see cref="TileKeySegment"/>.</exception>
    public TileCacheScope(string organizationScope, string grantScopeHash)
    {
        TileKeySegment.EnsureValid(organizationScope, nameof(organizationScope));
        TileKeySegment.EnsureValid(grantScopeHash, nameof(grantScopeHash));
        OrganizationScope = organizationScope;
        GrantScopeHash = grantScopeHash;
    }

    /// <summary>The caller's organization scope, e.g. its organization id.</summary>
    public string OrganizationScope { get; }

    /// <summary>The hash of the grants widening what the caller sees (<see cref="HashGrants"/>).</summary>
    public string GrantScopeHash { get; }

    /// <summary>Whether this is a real scope rather than <c>default</c>.</summary>
    public bool IsSpecified => OrganizationScope is not null;

    /// <summary>The scope of <paramref name="organizationScope"/> with <paramref name="grants"/> hashed by <see cref="HashGrants"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument or a grant is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="organizationScope"/> breaks <see cref="TileKeySegment"/>.</exception>
    public static TileCacheScope Create(string organizationScope, IEnumerable<string> grants) =>
        new(organizationScope, HashGrants(grants));

    /// <summary>
    /// The grant-scope hash of <paramref name="grants"/> (e.g. the ids of the organizations whose features a grant lets
    /// the caller see): lowercase hex SHA-256 of the distinct grants in ordinal order, each length-prefixed — so it
    /// depends on the set alone (not order or duplicates), and no two different sets hash alike by concatenation.
    /// No grants hash to a fixed value.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="grants"/> or a grant is <c>null</c>.</exception>
    public static string HashGrants(IEnumerable<string> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var ordered = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant, nameof(grants));
            ordered.Add(grant);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var grant in ordered)
        {
            var bytes = Encoding.UTF8.GetBytes(grant);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
