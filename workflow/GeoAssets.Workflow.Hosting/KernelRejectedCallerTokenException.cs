namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The kernel answered 401 to one of its caller-scoped identity routes although this host forwarded the caller's own
/// bearer token (XD01-390): this host accepted the token, the kernel did not — almost always because the kernel does not
/// accept the token's audience (this host's own API registration missing from the kernel's
/// <c>AzureAdCiam:AdditionalAudiences</c>). A deployment misconfiguration the caller cannot fix by signing in again;
/// <see cref="KernelFailureTranslation"/> answers it 502 <see cref="KernelFailureTranslation.TokenRejectedProblemCode"/>.
/// Carries what diagnoses it — the kernel's <c>WWW-Authenticate</c> <c>error</c>/<c>error_description</c>, the kernel
/// route, and the <c>aud</c>/<c>iss</c> claim values of the request's authenticated principal — never the token itself.
/// </summary>
public sealed class KernelRejectedCallerTokenException : Exception
{
    public KernelRejectedCallerTokenException()
        : this("The GeoAssets kernel rejected the caller's token.")
    {
    }

    public KernelRejectedCallerTokenException(string message)
        : base(message)
    {
    }

    public KernelRejectedCallerTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public KernelRejectedCallerTokenException(
        string? kernelPath, string? kernelError, string? kernelErrorDescription, string? tokenAudience, string? tokenIssuer)
        : base(Describe(kernelPath, kernelError, kernelErrorDescription, tokenAudience, tokenIssuer))
    {
        KernelPath = kernelPath;
        KernelError = kernelError;
        KernelErrorDescription = kernelErrorDescription;
        TokenAudience = tokenAudience;
        TokenIssuer = tokenIssuer;
    }

    /// <summary>The kernel route that answered 401 (its path, e.g. <c>/api/identity/me</c>).</summary>
    public string? KernelPath { get; }

    /// <summary>The <c>error</c> parameter of the kernel's <c>WWW-Authenticate</c> header (e.g. <c>invalid_token</c>), when present.</summary>
    public string? KernelError { get; }

    /// <summary>The <c>error_description</c> parameter of the kernel's <c>WWW-Authenticate</c> header, when present.</summary>
    public string? KernelErrorDescription { get; }

    /// <summary>The caller token's <c>aud</c> claim value(s), as this host's authenticated principal carries them (comma-separated).</summary>
    public string? TokenAudience { get; }

    /// <summary>The caller token's <c>iss</c> claim value, as this host's authenticated principal carries it.</summary>
    public string? TokenIssuer { get; }

    private static string Describe(
        string? kernelPath, string? kernelError, string? kernelErrorDescription, string? tokenAudience, string? tokenIssuer) =>
        $"The GeoAssets kernel answered 401 to {kernelPath ?? "(unknown route)"} for the caller's forwarded token " +
        $"(token audience '{tokenAudience ?? "(none)"}', issuer '{tokenIssuer ?? "(none)"}', " +
        $"kernel error '{kernelError ?? "(none)"}': {kernelErrorDescription ?? "(no description)"}).";
}
