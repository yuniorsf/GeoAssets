namespace GeoAssets.Core.Rendering;

/// <summary>
/// The rule for an identifier that becomes one segment of a tile URL or tile cache key (XD01-431) — a layer set id, a
/// layer id, a layer version, an organization scope, a grant-scope hash: 1 to <see cref="MaxLength"/> characters among
/// ASCII letters, digits, <c>.</c>, <c>_</c>, <c>~</c> and <c>-</c> (RFC 3986 unreserved, so it never needs escaping
/// and can't hold the <c>/</c> that separates segments), and not <c>.</c> or <c>..</c>.
/// </summary>
public static class TileKeySegment
{
    /// <summary>The longest a segment may be.</summary>
    public const int MaxLength = 128;

    /// <summary>Whether <paramref name="value"/> may be a tile URL / cache key segment.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value is "." or "..")
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '~' or '-'))
                return false;
        }

        return true;
    }

    /// <summary>Throws unless <paramref name="value"/> is a valid segment.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> breaks the segment rule.</exception>
    public static void EnsureValid(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (!IsValid(value))
            throw new ArgumentException(
                $"'{value}' is not a valid tile key segment: 1 to {MaxLength} of A-Z a-z 0-9 . _ ~ -, and not '.' or '..'.",
                paramName);
    }
}
