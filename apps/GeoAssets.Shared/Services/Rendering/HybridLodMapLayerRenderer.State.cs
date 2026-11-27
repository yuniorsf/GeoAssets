using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

// The per-map state of HybridLodMapLayerRenderer (XD01-436).
public sealed partial class HybridLodMapLayerRenderer
{
    /// <summary>A map this renderer draws entries on.</summary>
    private sealed class MapState(string divId)
    {
        public string DivId { get; } = divId;
        public Dictionary<Guid, Binding> Entries { get; } = [];

        /// <summary>Whether <see cref="IMapTileLayerInterop.ShowTileLayersAsync"/> was asked since the map's state began.</summary>
        public bool Shown { get; set; }

        /// <summary>The style's answer; null while unreadable (or not asked yet).</summary>
        public MapTileStyleInfo? Info { get; set; }

        public string? Version { get; set; }
        public int? Zoom { get; set; }

        /// <summary>Features this client added or updated, until their tiles carry the write.</summary>
        public Dictionary<string, PendingWrite> Written { get; } = new(StringComparer.Ordinal);

        /// <summary>Features this client deleted, until their tiles no longer carry them.</summary>
        public Dictionary<string, PendingWrite> Deleted { get; } = new(StringComparer.Ordinal);

        /// <summary>The overlay features this renderer drew.</summary>
        public HashSet<string> Drawn { get; } = new(StringComparer.Ordinal);

        /// <summary>The tile copies hidden on the map.</summary>
        public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A write: when it happened and the <c>{v}</c> the map drew then.</summary>
    private sealed record PendingWrite(DateTimeOffset At, string? Version);

    /// <summary>One entry on one map: the provider it reads by id, and its write subscriptions.</summary>
    private sealed class Binding
    {
        private readonly IAssetProvider _events;
        private readonly EventHandler<GeoFeature> _written;
        private readonly EventHandler<string> _deleted;

        public Binding(IAssetProvider events, IAssetProvider source, EventHandler<GeoFeature> written, EventHandler<string> deleted)
        {
            _events = events;
            Source = source;
            _written = written;
            _deleted = deleted;
            events.FeatureAdded += written;
            events.FeatureUpdated += written;
            events.FeatureDeleted += deleted;
        }

        /// <summary>What the overlay is read from by id (the active entry's decorated provider, else its own).</summary>
        public IAssetProvider Source { get; set; }

        public void Unsubscribe()
        {
            _events.FeatureAdded -= _written;
            _events.FeatureUpdated -= _written;
            _events.FeatureDeleted -= _deleted;
        }
    }
}
