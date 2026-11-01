namespace GeoAssets.Workflow.Infrastructure;

/// <summary>Thrown by <see cref="ManholeAuditValidator.EnsureCanComplete"/> when a <see cref="ManholeAudit"/>'s NR-33 checklist fails validation.</summary>
public sealed class ManholeAuditValidationException : InvalidOperationException
{
    public string? ConduitFeatureId { get; }
    public IReadOnlyList<string> Errors { get; }

    public ManholeAuditValidationException(string conduitFeatureId, IReadOnlyList<string> errors)
        : base($"Manhole audit for conduit '{conduitFeatureId}' failed NR-33 checklist validation: {string.Join("; ", errors)}")
    {
        ConduitFeatureId = conduitFeatureId;
        Errors = errors;
    }

    public ManholeAuditValidationException()
    {
        Errors = [];
    }

    public ManholeAuditValidationException(string message) : base(message)
    {
        Errors = [];
    }

    public ManholeAuditValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }
}
