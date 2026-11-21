using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.FieldService.Agents.Identity;

/// <summary>
/// Binds <see cref="AgentIdentityOptions"/> from <see cref="AgentIdentityOptions.SectionName"/>,
/// falling back to <see cref="AgentIdentityOptions.LegacySectionName"/> with a warning so
/// configuration written before XD01-406 keeps working.
/// </summary>
internal sealed partial class AgentIdentityOptionsSetup(
    IConfiguration               configuration,
    ILogger<AgentIdentityOptions> logger) : IConfigureOptions<AgentIdentityOptions>
{
    public void Configure(AgentIdentityOptions options)
    {
        var section = configuration.GetSection(AgentIdentityOptions.SectionName);
        var legacy  = configuration.GetSection(AgentIdentityOptions.LegacySectionName);

        if (section.Exists())
        {
            if (legacy.Exists())
                LogLegacySectionIgnored(logger, AgentIdentityOptions.LegacySectionName, AgentIdentityOptions.SectionName);

            section.Bind(options);
            return;
        }

        if (legacy.Exists())
        {
            LogLegacySectionRead(logger, AgentIdentityOptions.LegacySectionName, AgentIdentityOptions.SectionName);
            legacy.Bind(options);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent identities were read from the legacy configuration section {LegacySection}; rename it to {Section}, the legacy name will stop being read in a later release")]
    private static partial void LogLegacySectionRead(ILogger logger, string legacySection, string section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The legacy configuration section {LegacySection} is ignored because {Section} is present; remove it")]
    private static partial void LogLegacySectionIgnored(ILogger logger, string legacySection, string section);
}
