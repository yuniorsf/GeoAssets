namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>Full GSE field names, layer names and key namespaces the mappers read.</summary>
internal static class GseFields
{
    public const string ProfileId = "neoenergia-gse";

    public const string LayerRedeBt = "REDE_BT";
    public const string LayerRedeMt = "REDE_MT";
    public const string LayerRedeLt = "REDE_LT";
    public const string LayerInstalacao = "INSTALACAO";
    public const string LayerPoste = "POSTE";

    public const string NamespacePg = "PG";
    public const string NamespaceInst = "INST";
    public const string NamespaceChave = "CHAVE";
    public const string NamespaceTrechoBt = "TRECHO_BT";
    public const string NamespaceTrechoMt = "TRECHO_MT";
    public const string NamespaceTrechoLt = "TRECHO_LT";

    public const string PgId = "NU_PG_ID";
    public const string PgType = "CD_TIPO_PG";
    public const string InstallationId = "NU_INSTALACAO_MT_ID";
    public const string SwitchId = "NU_CHAVE_MT_ID";
    public const string Acronym = "DE_SIGLA_1";
    public const string Plate = "DE_PLACA";
    public const string Node = "NU_NO";
    public const string ParentPgId = "NU_PG_P_ID";
    public const string ParentNode = "NU_NO_P";
    public const string StartPgId = "NU_PG_INICIAL_ID";
    public const string EndPgId = "NU_PG_FINAL_ID";
    public const string StartNode = "NU_NO_INICIAL";
    public const string EndNode = "NU_NO_FINAL";
    public const string MvSegmentId = "NU_TRECHO_MT_ID";
    public const string LvSegmentId = "NU_TRECHO_BT_ID";
    public const string HvSegmentId = "NU_TRECHO_LT_ID";

    public const string FeederHead = "feeder-head";
    public const string ProtectionZone = "protection-zone";
}
