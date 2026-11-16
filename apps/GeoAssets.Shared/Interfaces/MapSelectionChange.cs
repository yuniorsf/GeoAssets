namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// What one <see cref="IMapSelectionSet"/> operation did (XD01-310).
/// </summary>
/// <param name="Added">Ids that entered the selection, in the order they were given.</param>
/// <param name="Removed">Ids that left the selection.</param>
/// <param name="Overflow">How many distinct new ids were left out because the selection was at
/// <see cref="IMapSelectionSet.MaxCount"/>. Greater than zero must be shown to the user.</param>
public sealed record MapSelectionChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int Overflow)
{
    /// <summary>The operation changed nothing and left nothing out.</summary>
    public static MapSelectionChange None { get; } = new([], [], 0);

    /// <summary>Whether the operation neither changed the selection nor left ids out.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Overflow == 0;
}
