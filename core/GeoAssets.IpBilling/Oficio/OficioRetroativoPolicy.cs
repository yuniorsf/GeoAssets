namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// <para>
/// <b>FLAGGED BUSINESS-OWNER CHECKPOINT (XD01-185) — not silently decided by this ticket.</b>
/// </para>
/// <para>
/// Fiscalização's <c>Retroativo = Retroativo₁ − Retroativo₂</c> (excluding groups 3/4) is a
/// <em>confirmed deliberate</em> legacy rule (XD01-182). For Ofício, the legacy spreadsheet
/// drops groups 3/4 from Retroativo the same way — but it is explicitly unclear whether that
/// mirrors a deliberate Ofício-specific rule, or was an oversight in the legacy tool specific to
/// Fiscalização's own column layout that happens to look the same here by coincidence. This
/// ticket does not resolve that ambiguity; it only makes the choice a one-line, reviewable
/// change instead of something baked silently into the arithmetic.
/// </para>
/// <para>
/// <see cref="OficioLineCalculator.DefaultRetroativoPolicy"/> currently defaults to
/// <see cref="SymmetricWithAgregado"/> (i.e. groups 3/4 ARE included in Retroativo) — the
/// opposite default from Fiscalização. If the business owner confirms Ofício should mirror
/// Fiscalização's asymmetric exclusion instead, flip that one constant to
/// <see cref="ExcludeGroups3And4"/>.
/// </para>
/// </summary>
public enum OficioRetroativoPolicy
{
    /// <summary>Retroativo = Agregado₁ − Agregado₂ + Agregado₃ − Agregado₄ (current default).</summary>
    SymmetricWithAgregado = 0,

    /// <summary>Fiscalização-style: Retroativo = Agregado₁ − Agregado₂ only.</summary>
    ExcludeGroups3And4 = 1,
}
