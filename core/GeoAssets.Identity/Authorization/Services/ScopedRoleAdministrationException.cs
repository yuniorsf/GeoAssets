namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// A refusal of a scoped-role administration route (XD01-327), raised by <see cref="IScopedRoleAdministrationClient"/>:
/// the HTTP status and, when the server named one, the <see cref="ScopedRoleAssignmentProblems"/> code.
/// </summary>
public sealed class ScopedRoleAdministrationException : Exception
{
    public ScopedRoleAdministrationException()
    {
    }

    public ScopedRoleAdministrationException(string message) : base(message)
    {
    }

    public ScopedRoleAdministrationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ScopedRoleAdministrationException(int statusCode, string? code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <summary>The HTTP status code of the refusal.</summary>
    public int StatusCode { get; }

    /// <summary>One of <see cref="ScopedRoleAssignmentProblems"/>, or null when the response named none.</summary>
    public string? Code { get; }
}
