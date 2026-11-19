namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Which change sets of one service order <see cref="IOccupancyChangeSetReader.GetPageAsync"/>
/// returns, and which page of them. Invalid paging is rejected here — on construction and on
/// every <c>with</c> change — so every adapter fails the same way; values are never clamped.
/// </summary>
public sealed record OccupancyChangeSetQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <exception cref="ArgumentException"><paramref name="orderId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is below 1, <paramref name="pageSize"/> is outside
    /// 1..<see cref="MaxPageSize"/>, or <paramref name="status"/> is not a defined value.
    /// </exception>
    public OccupancyChangeSetQuery(
        string orderId, int page = 1, int pageSize = DefaultPageSize, OccupancyChangeSetStatus? status = null)
    {
        OrderId = orderId;
        Page = page;
        PageSize = pageSize;
        Status = status;
    }

    /// <summary>The service order whose change sets are listed.</summary>
    public string OrderId
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>1-based page number.</summary>
    public int Page
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    }

    /// <summary>Change sets per page, 1..<see cref="MaxPageSize"/>.</summary>
    public int PageSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxPageSize);
            field = value;
        }
    }

    /// <summary>Null lists the order's sets in every status; otherwise only those in this one.</summary>
    public OccupancyChangeSetStatus? Status
    {
        get;
        init
        {
            if (value is { } status && !Enum.IsDefined(status))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown change set status.");
            field = value;
        }
    }

    /// <summary>Number of change sets before this page: <c>(Page - 1) * PageSize</c>.</summary>
    public int Skip => checked((Page - 1) * PageSize);
}
