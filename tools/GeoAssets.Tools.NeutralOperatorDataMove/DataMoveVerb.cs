namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>What one run does.</summary>
internal enum DataMoveVerb
{
    /// <summary>Dry run (the default): counts per table, conflicts and preconditions; writes nothing.</summary>
    Plan,

    /// <summary>Inserts every moved row absent from the target, keeping its values; skips identical rows; never overwrites.</summary>
    Copy,

    /// <summary>Checks that every moved row is in the target with identical content; writes nothing.</summary>
    Verify,

    /// <summary>Re-verifies and, in the same kernel transaction, deletes the moved rows from the kernel (needs <c>--confirm</c>).</summary>
    Remove,
}
