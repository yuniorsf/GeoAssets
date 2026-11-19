namespace GeoAssets.Identity.Authorization.Modules;

/// <summary>
/// A permission a module contributes (XD01-377): its <paramref name="Code"/> (<c>resource:action</c>), the resource and
/// action it is made of, and the i18n key of its description.
/// </summary>
public sealed record PermissionDefinition(string Code, string Resource, string Action, string DescriptionKey);
