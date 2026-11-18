using GeoAssets.Hosting.Abstractions;

namespace GeoAssets.Server;

/// <summary>
/// The host's exception → problem answer (XD01-363): the first registered <see cref="IProblemDetailsMapper"/> that answers,
/// else the generic workflow answers of <see cref="WorkflowProblemResults.FromException"/>; null when neither does, so a
/// failure that is not client-facing still propagates.
/// </summary>
public sealed class ProblemDetailsMapperChain(IEnumerable<IProblemDetailsMapper> mappers)
{
    private readonly IProblemDetailsMapper[] _mappers = [.. mappers ?? throw new ArgumentNullException(nameof(mappers))];

    public IResult? TryMap(Exception exception, ProblemContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var mapper in _mappers)
        {
            if (mapper.TryMap(exception, context) is { } result)
            {
                return result;
            }
        }

        return WorkflowProblemResults.FromException(exception, context.OrderId ?? string.Empty);
    }
}
