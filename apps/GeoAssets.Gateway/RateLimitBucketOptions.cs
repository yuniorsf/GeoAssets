using System.Threading.RateLimiting;

namespace GeoAssets.Gateway;

/// <summary>
/// One policy's token bucket (XD01-393): at most <paramref name="TokenLimit"/> requests in a burst, refilled by
/// <paramref name="TokensPerPeriod"/> every <paramref name="ReplenishmentPeriodSeconds"/> seconds; no queue (a request
/// beyond the bucket is answered 429 at once).
/// </summary>
internal sealed record RateLimitBucketOptions(int TokenLimit, int TokensPerPeriod, int ReplenishmentPeriodSeconds)
{
    public TimeSpan ReplenishmentPeriod => TimeSpan.FromSeconds(ReplenishmentPeriodSeconds);

    /// <summary>
    /// The bucket configured in <paramref name="section"/>, each key absent there taken from <paramref name="fallback"/>.
    /// Throws at startup when a value is below 1 or not a number.
    /// </summary>
    /// <param name="section">The configuration section holding <c>TokenLimit</c>, <c>TokensPerPeriod</c>, <c>ReplenishmentPeriodSeconds</c>.</param>
    /// <param name="fallback">The values of the keys <paramref name="section"/> doesn't set.</param>
    public static RateLimitBucketOptions Read(IConfiguration section, RateLimitBucketOptions fallback)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(fallback);

        var options = new RateLimitBucketOptions(
            section.GetValue(nameof(TokenLimit), fallback.TokenLimit),
            section.GetValue(nameof(TokensPerPeriod), fallback.TokensPerPeriod),
            section.GetValue(nameof(ReplenishmentPeriodSeconds), fallback.ReplenishmentPeriodSeconds));

        if (options.TokenLimit < 1 || options.TokensPerPeriod < 1 || options.ReplenishmentPeriodSeconds < 1)
        {
            var path = section is IConfigurationSection configurationSection ? configurationSection.Path : "the configuration root";
            throw new InvalidOperationException(
                $"Rate-limit configuration '{path}' needs TokenLimit, TokensPerPeriod and ReplenishmentPeriodSeconds of at least 1 (got {options}).");
        }

        return options;
    }

    public TokenBucketRateLimiterOptions ToLimiterOptions() => new()
    {
        TokenLimit = TokenLimit,
        TokensPerPeriod = TokensPerPeriod,
        ReplenishmentPeriod = ReplenishmentPeriod,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
