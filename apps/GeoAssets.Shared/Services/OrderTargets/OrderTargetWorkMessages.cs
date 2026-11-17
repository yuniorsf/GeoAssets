using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>The message keys of the per-target work editor (XD01-313), kept out of the component so every branch is testable.</summary>
public static class OrderTargetWorkMessages
{
    /// <summary>The key shown when the target changed since it was read, and the editor now shows the newer values.</summary>
    public const string ConcurrentKey = "targetWork.error.concurrent";

    /// <summary>The key shown when the target changed meanwhile and could not be read again.</summary>
    public const string ConcurrentReloadFailedKey = "targetWork.error.concurrentReloadFailed";

    /// <summary>The key shown when the target, or its order, is gone.</summary>
    public const string NotFoundKey = "targetWork.error.notFound";

    /// <summary>
    /// The message key for a failed save, by the exception <c>IServiceOrderTargetWriter.UpdateAsync</c>
    /// (or its REST client) throws for each problem. Anything unrecognised gets the generic one.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is null.</exception>
    public static string ErrorKey(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ServiceOrderConcurrencyException => ConcurrentKey,
            OrderActionDeniedException => "targetWork.error.denied",
            KeyNotFoundException => NotFoundKey,
            ArgumentException or InvalidOperationException => "targetWork.error.invalid",
            _ => "targetWork.error.failed",
        };
    }

    /// <summary>Every key <see cref="ErrorKey"/> can return, and the two a save adds after a conflict.</summary>
    public static IReadOnlyList<string> ErrorKeys { get; } =
    [
        ConcurrentKey, ConcurrentReloadFailedKey, NotFoundKey, "targetWork.error.denied", "targetWork.error.invalid", "targetWork.error.failed",
    ];
}
