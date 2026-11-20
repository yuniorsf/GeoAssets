using System.Net;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Translates a failed call to the kernel (XD01-385: the caller's permission check at <c>/api/identity/me</c>, the
/// generic grant writes) into an answer the caller can act on instead of an unhandled 500: the kernel refusing what this
/// host sent on the caller's behalf (400) is the caller's bad request; anything else — the kernel unreachable, a 5xx, or
/// the kernel refusing this host's own credentials or the forwarded token's audience (401/403) — is this host's
/// dependency being unavailable, 503 <c>kernel.unavailable</c>.
/// </summary>
internal static partial class KernelFailureTranslation
{
    public const string UnavailableProblemCode = "kernel.unavailable";
    public const string RejectedProblemCode = "kernel.rejected";

    /// <summary>Wraps the rest of the pipeline; place before authentication so the permission check is covered.</summary>
    public static IApplicationBuilder UseKernelFailureTranslation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (!context.Response.HasStarted)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KernelFailureTranslation).FullName!);
                var rejected = ex.StatusCode == HttpStatusCode.BadRequest;
                LogKernelFailure(logger, context.Request.Path, (int?)ex.StatusCode, ex);

                var problem = rejected
                    ? Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The kernel refused the request.",
                        detail: ex.Message,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = RejectedProblemCode })
                    : Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "The GeoAssets kernel is unavailable.",
                        detail: "A call to the kernel failed; retry later.",
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = UnavailableProblemCode });
                await problem.ExecuteAsync(context).ConfigureAwait(false);
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A call to the kernel failed while serving {Path} (kernel status {KernelStatus}).")]
    private static partial void LogKernelFailure(ILogger logger, string path, int? kernelStatus, Exception exception);
}
