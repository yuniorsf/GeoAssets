namespace GeoAssets.NeutralOperator.Modules.Occupancy;

/// <summary>The approval saga's timeout settings (XD01-383), bound from <see cref="SectionName"/>.</summary>
public sealed class OccupancyApplySagaOptions
{
    /// <summary>The configuration section: <c>Saga:ApplyTimeout</c>, <c>Saga:SweepInterval</c>, ….</summary>
    public const string SectionName = "Saga";

    /// <summary>How long an approval may wait for the kernel's reply (since its batch was last staged) before it is flagged as timed out.</summary>
    public TimeSpan ApplyTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the sweeper looks for timed-out sagas and measures the backlog.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How many sagas one sweep flags at most per page; a sweep reads at most <see cref="MaxPagesPerSweep"/> pages.</summary>
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>The most pages one sweep reads; what is left is flagged on the next sweep.</summary>
    public int MaxPagesPerSweep { get; set; } = 10;

    /// <summary>Whether the sweeper runs at all (a test host may switch it off).</summary>
    public bool EnableSweeper { get; set; } = true;
}
