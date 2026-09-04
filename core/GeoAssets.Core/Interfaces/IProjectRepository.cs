namespace GeoAssets.Core.Interfaces;

/// <summary>Composes <see cref="IProjectReader"/> and <see cref="IProjectWriter"/> for the common
/// case of needing full read/write access to <see cref="Models.Project"/>.</summary>
public interface IProjectRepository : IProjectReader, IProjectWriter;
