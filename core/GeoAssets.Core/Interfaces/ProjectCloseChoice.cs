namespace GeoAssets.Core.Interfaces;

/// <summary>
/// The three choices a caller resolves <see cref="IProjectSessionService.CloseRequested"/> with
/// when <see cref="IProjectSessionService.RequestCloseAsync"/> finds the session dirty.
/// </summary>
public enum ProjectCloseChoice
{
    SaveAndClose,
    DiscardAndClose,
    Cancel,
}
