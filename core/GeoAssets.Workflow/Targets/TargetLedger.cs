using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The global target ledger's vocabulary (XD01-379, ADR-001 §3.4): every <see cref="ServiceOrderTarget"/> is keyed by
/// <c>(vertical, order id)</c>. Orders of this host's own order types — the ones in <see cref="Orders.OrderTypeRegistry"/>,
/// whose targets the <c>/api/workflow/service-orders/{id}/targets</c> routes manage — belong to
/// <see cref="KernelVerticalId"/>; a remote vertical reserves, releases and commits its orders' targets through the
/// kernel's ledger API under its own id.
/// </summary>
public static class TargetLedger
{
    /// <summary>The vertical of in-process orders: every target written before the ledger existed, and every one the in-process routes write.</summary>
    public const string KernelVerticalId = "kernel";

    /// <summary>
    /// The problem code of a ledger reservation refused because features are held, in any vertical, with blocking
    /// exclusivity (409; the body's <c>conflicts</c> lists them). The ledger API's own: the in-process target routes'
    /// vocabulary (<see cref="WorkflowProblemCodes"/>) has no such refusal.
    /// </summary>
    public const string ConflictProblemCode = "targets.conflict";

    /// <summary>The longest vertical id the ledger stores.</summary>
    public const int MaxVerticalIdLength = 64;

    /// <summary>The longest order id the ledger stores (the target store's order-id column).</summary>
    public const int MaxOrderIdLength = 36;

    /// <summary>
    /// Whether <paramref name="verticalId"/> is a well-formed vertical id: lower-case letters and digits in
    /// hyphen-separated words, at most <see cref="MaxVerticalIdLength"/> characters. <see cref="KernelVerticalId"/> is
    /// well-formed; whether a caller may name it is the caller's rule (<see cref="IsRemoteVerticalId"/>).
    /// </summary>
    public static bool IsWellFormedVerticalId(string? verticalId)
    {
        // ^[a-z0-9]+(?:-[a-z0-9]+)*$, spelled out: the engine assembly holds no generated regex types (XD01-376).
        if (string.IsNullOrEmpty(verticalId) || verticalId.Length > MaxVerticalIdLength) return false;
        if (verticalId[0] == '-' || verticalId[^1] == '-') return false;

        for (var i = 0; i < verticalId.Length; i++)
        {
            var c = verticalId[i];
            if (c == '-')
            {
                if (verticalId[i - 1] == '-') return false;
            }
            else if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A well-formed vertical id other than <see cref="KernelVerticalId"/>: what a remote vertical may act under.</summary>
    public static bool IsRemoteVerticalId(string? verticalId) =>
        IsWellFormedVerticalId(verticalId) && !IsKernel(verticalId);

    /// <summary>Whether <paramref name="verticalId"/> is <see cref="KernelVerticalId"/> (ordinal).</summary>
    public static bool IsKernel(string? verticalId) => string.Equals(verticalId, KernelVerticalId, StringComparison.Ordinal);

    /// <summary>
    /// The id of the target that holds <paramref name="featureId"/> in <paramref name="verticalId"/>'s order
    /// <paramref name="orderId"/>: for the kernel exactly <see cref="OrderTargetSelectionService.TargetId"/>, for a remote
    /// vertical a GUID derived from all three, so two verticals' orders that share an id never share a target id. Each part
    /// is length-prefixed before hashing, so free-text order and feature ids can never be re-split into another triple
    /// (<c>("isp", "a|b", "c")</c> and <c>("isp", "a", "b|c")</c> hash different inputs).
    /// </summary>
    public static string TargetId(string verticalId, string orderId, string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        if (IsKernel(verticalId)) return OrderTargetSelectionService.TargetId(orderId, featureId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ledger|{verticalId.Length}:{verticalId}|{orderId.Length}:{orderId}|{featureId.Length}:{featureId}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
