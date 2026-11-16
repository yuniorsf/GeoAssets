using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

public sealed class ServiceOrderTargetBuilder
{
    public static readonly Guid DefaultOrganizationId = new("0a0a0a0a-0000-4000-8000-000000000001");

    private string _id = "tgt-100";
    private string _orderId = "ord-100";
    private string _featureId = "feat-100";
    private string _assetTypeId = "pole";
    private string? _alias = "P-100";
    private IReadOnlyList<string> _activityIds = [];
    private string _status = OrderTargetStatus.Pending;
    private OrderTargetConflictLevel _conflictLevel = OrderTargetConflictLevel.None;
    private string? _note;
    private double? _longitude = -38.5;
    private double? _latitude = -12.97;
    private DateTime _addedAt = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc.UtcDateTime;
    private string _addedBy = "operator-1";
    private Guid _organizationId = DefaultOrganizationId;
    private int _version;

    public ServiceOrderTargetBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public ServiceOrderTargetBuilder WithOrderId(string orderId)
    {
        _orderId = orderId;
        return this;
    }

    public ServiceOrderTargetBuilder WithFeatureId(string featureId)
    {
        _featureId = featureId;
        return this;
    }

    public ServiceOrderTargetBuilder WithAssetTypeId(string assetTypeId)
    {
        _assetTypeId = assetTypeId;
        return this;
    }

    public ServiceOrderTargetBuilder WithAlias(string? alias)
    {
        _alias = alias;
        return this;
    }

    public ServiceOrderTargetBuilder WithActivityIds(params string[] activityIds)
    {
        _activityIds = activityIds;
        return this;
    }

    public ServiceOrderTargetBuilder WithStatus(string status)
    {
        _status = status;
        return this;
    }

    public ServiceOrderTargetBuilder WithConflictLevel(OrderTargetConflictLevel conflictLevel)
    {
        _conflictLevel = conflictLevel;
        return this;
    }

    public ServiceOrderTargetBuilder WithNote(string? note)
    {
        _note = note;
        return this;
    }

    public ServiceOrderTargetBuilder WithPosition(double longitude, double latitude)
    {
        _longitude = longitude;
        _latitude = latitude;
        return this;
    }

    public ServiceOrderTargetBuilder WithoutPosition()
    {
        _longitude = null;
        _latitude = null;
        return this;
    }

    public ServiceOrderTargetBuilder WithAddedAt(DateTime addedAt)
    {
        _addedAt = addedAt;
        return this;
    }

    public ServiceOrderTargetBuilder WithAddedBy(string addedBy)
    {
        _addedBy = addedBy;
        return this;
    }

    public ServiceOrderTargetBuilder WithOrganizationId(Guid organizationId)
    {
        _organizationId = organizationId;
        return this;
    }

    public ServiceOrderTargetBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    public ServiceOrderTarget Build() =>
        new()
        {
            Id = _id,
            OrderId = _orderId,
            FeatureId = _featureId,
            AssetTypeId = _assetTypeId,
            Alias = _alias,
            ActivityIds = _activityIds,
            Status = _status,
            ConflictLevel = _conflictLevel,
            Note = _note,
            Longitude = _longitude,
            Latitude = _latitude,
            AddedAt = _addedAt,
            AddedBy = _addedBy,
            OrganizationId = _organizationId,
            Version = _version,
        };
}
