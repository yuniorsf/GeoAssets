using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.Server;

/// <summary>
/// Bulk feature import for trusted service callers: a caller streams an organization's features in chunks of at most
/// <see cref="FeatureImportLimits.MaxChunkSize"/>, then completes the import, through <see cref="IFeatureBulkImporter"/>.
/// The routes are domain-agnostic — the import names its organization, an <c>importId</c> of its choosing and the
/// <c>scope</c> it covers; the kernel stamps both on every feature it writes.
///
/// Like <see cref="OrganizationScopedFeaturesRestApiExtensions"/>, every route requires the
/// <see cref="GeoAuthorizationHandler.ServiceCallerAppRole"/> app role and nothing else: only a trusted internal service
/// principal may name the organization written for.
///
/// Routes (under <c>/api/geoassets</c> by default):
/// <list type="bullet">
///   <item><c>POST organizations/{organizationId}/feature-imports/{importId}/chunks?scope=</c> — body
///   <c>application/x-ndjson</c>, one feature per non-blank line in the asset routes' JSON
///   (<see cref="GeoJsonSerializer.Options"/>), optionally <c>Content-Encoding: gzip</c>; answers the
///   <see cref="FeatureImportChunkResult"/> (camelCase).</item>
///   <item><c>POST organizations/{organizationId}/feature-imports/{importId}/complete</c> — body
///   <c>{"scope":"…","mode":"merge"|"replaceScope"}</c>; answers the <see cref="FeatureImportCompletionResult"/>.</item>
/// </list>
/// Refusals are problem details with a <c>code</c> extension: 400 <see cref="InvalidImportIdCode"/>,
/// <see cref="InvalidScopeCode"/>, <see cref="InvalidFeatureCode"/>, <see cref="InvalidOrganizationCode"/> or
/// <see cref="InvalidRequestCode"/>; 413 <see cref="ChunkTooLargeCode"/>; 503 <see cref="UnavailableCode"/> on a host composed
/// without an importer (the <c>postgres-spatial</c> module registers it).
/// </summary>
public static class FeatureImportRestApiExtensions
{
    public const string InvalidImportIdCode = "feature-import.invalid-import-id";
    public const string InvalidScopeCode = "feature-import.invalid-scope";
    public const string InvalidFeatureCode = "feature-import.invalid-feature";
    public const string ChunkTooLargeCode = "feature-import.chunk-too-large";
    public const string InvalidOrganizationCode = "feature-import.invalid-organization";
    public const string InvalidRequestCode = "feature-import.invalid-request";
    public const string UnavailableCode = "feature-import.unavailable";

    private static readonly JsonSerializerOptions _resultJson = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions _completionJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static IEndpointRouteBuilder MapFeatureImportApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets")
    {
        var group = $"{prefix}/organizations/{{organizationId:guid}}/feature-imports/{{importId}}";

        routes.MapPost($"{group}/chunks", ImportChunkAsync)
            .RequireAuthorization(policy => policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole));

        routes.MapPost($"{group}/complete", CompleteAsync)
            .RequireAuthorization(policy => policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole));

        return routes;
    }

    private static async Task<IResult> ImportChunkAsync(
        Guid organizationId,
        string importId,
        string? scope,
        HttpRequest request,
        [FromServices] IFeatureBulkImporter? importer,
        CancellationToken ct)
    {
        if (importer is null)
            return Unavailable();

        if ((FindImportIdProblem(organizationId, importId) ?? FindScopeProblem(scope)) is { } problem)
            return problem;

        var (features, refusal) = await ReadFeaturesAsync(request, ct).ConfigureAwait(false);
        if (refusal is not null)
            return refusal;

        var result = await importer
            .ImportChunkAsync(new FeatureImportChunkRequest(organizationId, importId, scope!, features), ct)
            .ConfigureAwait(false);
        return Results.Json(result, _resultJson);
    }

    private static async Task<IResult> CompleteAsync(
        Guid organizationId,
        string importId,
        HttpRequest request,
        [FromServices] IFeatureBulkImporter? importer,
        CancellationToken ct)
    {
        if (importer is null)
            return Unavailable();

        if (FindImportIdProblem(organizationId, importId) is { } importIdProblem)
            return importIdProblem;

        FeatureImportCompletionBody? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<FeatureImportCompletionBody>(request.Body, _completionJson, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null)
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidRequestCode,
                "The body must be {\"scope\":\"…\",\"mode\":\"merge\"|\"replaceScope\"}.");
        }

        if (FindScopeProblem(body.Scope) is { } scopeProblem)
            return scopeProblem;

        var result = await importer
            .CompleteAsync(new FeatureImportCompletion(organizationId, importId, body.Scope!, body.Mode), ct)
            .ConfigureAwait(false);
        return Results.Json(result, _resultJson);
    }

    private static IResult? FindImportIdProblem(Guid organizationId, string importId)
    {
        if (organizationId == Guid.Empty)
            return Problem(StatusCodes.Status400BadRequest, InvalidOrganizationCode, "An import names a real organization.");

        if (!IsValidImportId(importId))
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidImportIdCode,
                $"The import id must be 1 to {FeatureImportLimits.MaxImportIdLength} characters of A-Z, a-z, 0-9, '.', '_' or '-'.");
        }

        return null;
    }

    private static IResult? FindScopeProblem(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > FeatureImportLimits.ScopeMaxLength)
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidScopeCode,
                $"The scope must be non-blank and at most {FeatureImportLimits.ScopeMaxLength} characters.");
        }

        return null;
    }

    private static bool IsValidImportId(string? importId) =>
        importId is { Length: > 0 and <= FeatureImportLimits.MaxImportIdLength }
        && importId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>Reads the NDJSON body (gunzipped when <c>Content-Encoding: gzip</c>): the features, or the refusal to answer.</summary>
    private static async Task<(List<GeoFeature> Features, IResult? Refusal)> ReadFeaturesAsync(HttpRequest request, CancellationToken ct)
    {
        if (!IsGzip(request))
            return await ReadLinesAsync(request.Body, ct).ConfigureAwait(false);

        var gunzipped = new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);
        await using (gunzipped.ConfigureAwait(false))
            return await ReadLinesAsync(gunzipped, ct).ConfigureAwait(false);
    }

    private static async Task<(List<GeoFeature> Features, IResult? Refusal)> ReadLinesAsync(Stream body, CancellationToken ct)
    {
        var features = new List<GeoFeature>();
        try
        {
            using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var lineNumber = 0;
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (features.Count == FeatureImportLimits.MaxChunkSize)
                {
                    return (features, Problem(StatusCodes.Status413PayloadTooLarge, ChunkTooLargeCode,
                        $"A chunk holds at most {FeatureImportLimits.MaxChunkSize} features."));
                }

                if (ParseFeature(line) is not { } feature)
                {
                    return (features, Problem(StatusCodes.Status400BadRequest, InvalidFeatureCode,
                        $"Line {lineNumber} is not a valid feature."));
                }

                features.Add(feature);
            }
        }
        catch (InvalidDataException)
        {
            return (features, Problem(StatusCodes.Status400BadRequest, InvalidFeatureCode, "The gzip body is not valid."));
        }

        return (features, null);
    }

    private static GeoFeature? ParseFeature(string line)
    {
        GeoFeature? feature;
        try
        {
            feature = JsonSerializer.Deserialize<GeoFeature>(line, GeoJsonSerializer.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        return feature?.Properties is { } properties
               && (string.IsNullOrEmpty(properties.LayerId) || Guid.TryParse(properties.LayerId, out _))
            ? feature
            : null;
    }

    private static bool IsGzip(HttpRequest request) =>
        request.Headers.ContentEncoding
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(encoding => string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase));

    private static IResult Unavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, UnavailableCode, "This host has no bulk feature importer.");

    private static IResult Problem(int statusCode, string code, string detail) =>
        Results.Problem(
            statusCode: statusCode,
            title: "The feature import request was refused.",
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });
}
