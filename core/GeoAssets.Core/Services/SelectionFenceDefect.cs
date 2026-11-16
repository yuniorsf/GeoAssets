namespace GeoAssets.Core.Services;

/// <summary>Why <see cref="SelectionFenceValidator"/> rejected a selection fence.</summary>
public enum SelectionFenceDefect
{
    /// <summary>The fence has no rings at all.</summary>
    Empty,

    /// <summary>A ring is missing, or a position is not exactly a longitude/latitude pair.</summary>
    MalformedPosition,

    /// <summary>A coordinate is NaN or infinite.</summary>
    NonFiniteCoordinate,

    /// <summary>A longitude is outside [-180, 180] or a latitude is outside [-90, 90].</summary>
    OutOfRange,

    /// <summary>A ring has fewer than the four positions a closed ring needs.</summary>
    TooFewVertices,

    /// <summary>A ring's first and last positions differ.</summary>
    NotClosed,

    /// <summary>The fence has more vertices, over all rings, than the caller allows.</summary>
    TooManyVertices,

    /// <summary>The exterior ring spans more than 180° of longitude.</summary>
    CrossesAntimeridian,

    /// <summary>The polygon is topologically invalid (self-intersection, hole outside the shell, …).</summary>
    SelfIntersecting,

    /// <summary>The polygon encloses no area.</summary>
    ZeroArea,
}
