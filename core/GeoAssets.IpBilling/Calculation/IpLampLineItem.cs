using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

public sealed record IpLampLineItem(
    LampTypeCode Code,
    int WattageW,
    int Quantity);
