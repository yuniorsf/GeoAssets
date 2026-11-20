using System.Net;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// The kernel refused an organization-grant upsert with 409 <c>organization-grant.conflict</c> (XD01-394): concurrent
/// writes of the same key outran its one transparent retry. Repeating the request is safe. An
/// <see cref="HttpRequestException"/> with <see cref="HttpStatusCode.Conflict"/>, so a caller that handles kernel failures
/// generically keeps working; a vertical host's <c>KernelFailureTranslation</c> answers it 409 <c>kernel.conflict</c> (XD01-401).
/// </summary>
public sealed class OrganizationGrantConflictException : HttpRequestException
{
    /// <summary>The kernel's problem code for this refusal (its <c>OrganizationGrantsRestApiExtensions.ConflictProblemCode</c>).</summary>
    public const string ProblemCode = "organization-grant.conflict";

    public OrganizationGrantConflictException()
        : this("The kernel refused an organization-grant upsert: concurrent writes of its key; repeat the request.")
    {
    }

    public OrganizationGrantConflictException(string message)
        : base(message, inner: null, HttpStatusCode.Conflict)
    {
    }

    public OrganizationGrantConflictException(string message, Exception innerException)
        : base(message, innerException, HttpStatusCode.Conflict)
    {
    }

    public OrganizationGrantConflictException(OrganizationGrantKey key, string? kernelDetail)
        : base(MessageFor(key), inner: null, HttpStatusCode.Conflict)
    {
        Key = key;
        KernelDetail = kernelDetail;
    }

    /// <summary>The key of the grant whose upsert was refused; null only when built with a message alone.</summary>
    public OrganizationGrantKey? Key { get; }

    /// <summary>The kernel problem's <c>detail</c>, if it sent one.</summary>
    public string? KernelDetail { get; }

    private static string MessageFor(OrganizationGrantKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"The kernel refused the organization-grant upsert of key {key}: concurrent writes of that key; repeat the request.";
    }
}
