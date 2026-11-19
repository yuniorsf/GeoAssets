namespace GeoAssets.NeutralOperator.UI.Services.SharedUse;

/// <summary>Something a dispatch plan leaves out: the localized string to show and the count it is about.</summary>
/// <param name="MessageKey">Localization key; its text takes <paramref name="Count"/> as <c>{0}</c>.</param>
/// <param name="Count">How many poles or attachments the caveat is about.</param>
public sealed record SharedUseDispatchCaveat(string MessageKey, int Count);
