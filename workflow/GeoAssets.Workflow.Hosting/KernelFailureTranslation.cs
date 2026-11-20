using System.Net;
using GeoAssets.Workflow.Rest;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Translates a failed call to the kernel (XD01-385: the caller's permission check at <c>/api/identity/me</c>, the
/// generic grant writes; moved into the chassis by XD01-390 for every vertical host) into an answer the caller can act on
/// instead of an unhandled 500:
/// <list type="bullet">
///   <item>the kernel rejecting the caller's forwarded token (<see cref="KernelRejectedCallerTokenException"/>, a kernel
///   401 to the caller's own token) — 502 <see cref="TokenRejectedProblemCode"/>: a deployment misconfiguration (the
///   kernel does not accept this host's audience), logged once at Error with what diagnoses it;</item>
///   <item>the kernel refusing what this host sent on the caller's behalf (an <see cref="HttpRequestException"/> with 400)
///   — 400 <see cref="RejectedProblemCode"/>;</item>
///   <item>the kernel refusing an organization-grant upsert because concurrent writes of its key raced it
///   (<see cref="OrganizationGrantConflictException"/>, a kernel 409 <c>organization-grant.conflict</c>, XD01-401) — 409
///   <see cref="ConflictProblemCode"/>: repeating the request is safe;</item>
///   <item>anything else — the kernel unreachable, a 5xx, or the kernel refusing this host's own credentials (401/403) —
///   this host's dependency being unavailable, 503 <see cref="UnavailableProblemCode"/>.</item>
/// </list>
/// </summary>
public static partial class KernelFailureTranslation
{
    public const string UnavailableProblemCode = "kernel.unavailable";
    public const string RejectedProblemCode = "kernel.rejected";
    public const string TokenRejectedProblemCode = "kernel.token-rejected";
    public const string ConflictProblemCode = "kernel.conflict";

    private const string TokenRejectedCause =
        "The kernel does not accept this token's audience — add it to the kernel's AzureAdCiam:AdditionalAudiences, or use the kernel's API audience.";

    /// <summary>Wraps the rest of the pipeline; place after CORS and before authentication so the permission check is covered.</summary>
    public static IApplicationBuilder UseKernelFailureTranslation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (KernelRejectedCallerTokenException ex) when (!context.Response.HasStarted)
            {
                LogKernelRejectedCallerToken(
                    Logger(context), context.Request.Path, ex.KernelPath, ex.TokenAudience, ex.TokenIssuer, ex.KernelError, ex.KernelErrorDescription);

                var detail = string.IsNullOrWhiteSpace(ex.KernelErrorDescription)
                    ? TokenRejectedCause
                    : $"{TokenRejectedCause} The kernel said: {ex.KernelErrorDescription}";
                await Results.Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "The GeoAssets kernel rejected the caller's token.",
                        detail: detail,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = TokenRejectedProblemCode })
                    .ExecuteAsync(context).ConfigureAwait(false);
            }
            catch (OrganizationGrantConflictException ex) when (!context.Response.HasStarted)
            {
                LogKernelConflict(Logger(context), context.Request.Path, ex);

                await Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "The kernel refused a concurrent write.",
                        detail: "Concurrent writes of the same organization grant raced this request; repeat it.",
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = ConflictProblemCode })
                    .ExecuteAsync(context).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (!context.Response.HasStarted)
            {
                var rejected = ex.StatusCode == HttpStatusCode.BadRequest;
                LogKernelFailure(Logger(context), context.Request.Path, (int?)ex.StatusCode, ex);

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

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KernelFailureTranslation).FullName!);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A call to the kernel failed while serving {Path} (kernel status {KernelStatus}).")]
    private static partial void LogKernelFailure(ILogger logger, string path, int? kernelStatus, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The kernel refused a concurrent organization-grant write while serving {Path}; the caller may repeat it.")]
    private static partial void LogKernelConflict(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message =
        "The GeoAssets kernel rejected the caller's token while serving {Path}: {KernelPath} answered 401 " +
        "(token audience {TokenAudience}, issuer {TokenIssuer}; kernel error {KernelError}: {KernelErrorDescription}). " +
        "The kernel likely does not accept this audience: add it to the kernel's AzureAdCiam:AdditionalAudiences, or have this host use the kernel's API audience.")]
    private static partial void LogKernelRejectedCallerToken(
        ILogger logger, string path, string? kernelPath, string? tokenAudience, string? tokenIssuer, string? kernelError, string? kernelErrorDescription);
}
