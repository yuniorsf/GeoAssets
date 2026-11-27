using GeoAssets.Core.Services;

namespace GeoAssets.Server;

/// <summary>
/// Answers a read that would enumerate too many features of a large, PostgreSQL-served organization catalog
/// (<see cref="CatalogEnumerationLimitException"/>, XD01-449) with <c>422</c> and the problem code
/// <see cref="ProblemCode"/> — the caller narrows the read (a bounding box, a page, a closer topology seed) — instead of an
/// unhandled 500. The problem carries the refused <c>operation</c> and the <c>limit</c>.
/// </summary>
public static partial class CatalogEnumerationLimitTranslationExtensions
{
    public const string ProblemCode = "catalog.enumeration-limit";

    /// <summary>Wraps the rest of the pipeline; place before the endpoints that read the organization catalog.</summary>
    public static IApplicationBuilder UseCatalogEnumerationLimitTranslation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (CatalogEnumerationLimitException ex) when (!context.Response.HasStarted)
            {
                LogRefused(
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CatalogEnumerationLimitTranslationExtensions)),
                    context.Request.Path,
                    ex.Operation,
                    ex.Limit);

                await Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Too many features for this read",
                        detail: ex.Message,
                        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["code"] = ProblemCode,
                            ["operation"] = ex.Operation,
                            ["limit"] = ex.Limit,
                        })
                    .ExecuteAsync(context).ConfigureAwait(false);
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Operation} refused, it would return more than {Limit} features of the organization catalog")]
    private static partial void LogRefused(ILogger logger, string path, string operation, int limit);
}
