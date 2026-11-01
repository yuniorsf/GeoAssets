namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>
/// PRODIST Módulo 8 Submódulo 8's individual continuity indicators for one consumer unit over
/// one apuração period:
/// <list type="bullet">
///   <item><see cref="Dic"/> — Duração de Interrupção Individual por Unidade Consumidora: total interrupted time.</item>
///   <item><see cref="Fic"/> — Frequência de Interrupção Individual por Unidade Consumidora: number of qualifying interruptions.</item>
///   <item><see cref="Dmic"/> — Duração Máxima de Interrupção Contínua por Unidade Consumidora: the single longest interruption.</item>
/// </list>
/// </summary>
public sealed record ContinuityIndicators(TimeSpan Dic, int Fic, TimeSpan Dmic);
