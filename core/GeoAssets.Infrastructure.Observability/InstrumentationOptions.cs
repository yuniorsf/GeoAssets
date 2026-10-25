namespace GeoAssets.Infrastructure.Observability;

public sealed class InstrumentationOptions
{
    /// <summary>Instrument EF Core commands as spans. Disable if the project has no EF dependency.</summary>
    public bool EnableEFCore { get; set; } = true;

    /// <summary>Collect .NET runtime metrics (GC, thread pool, JIT).</summary>
    public bool EnableRuntime { get; set; } = true;

    /// <summary>Collect process-level metrics (CPU, memory, handles).</summary>
    public bool EnableProcess { get; set; }

    /// <summary>Record HTTP 4xx/5xx responses as span exceptions.</summary>
    public bool RecordExceptionOnHttpErrors { get; set; } = true;
}
