using System.Runtime.Serialization;

namespace MbCnsTool.RuntimeLocalization;

[DataContract]
internal sealed class RuntimeLocalizationMapContract
{
    [DataMember(Name = "schemaVersion")]
    public int SchemaVersion { get; set; }

    [DataMember(Name = "targetLanguage")]
    public string? TargetLanguage { get; set; }

    [DataMember(Name = "generatedAtUtc")]
    public string? GeneratedAtUtc { get; set; }

    [DataMember(Name = "gameVersionGate")]
    public RuntimeLocalizationVersionGateContract? GameVersionGate { get; set; }

    [DataMember(Name = "entries")]
    public RuntimeLocalizationEntryContract[]? Entries { get; set; }
}

[DataContract]
internal sealed class RuntimeLocalizationVersionGateContract
{
    [DataMember(Name = "allowedCoreAssemblyVersions")]
    public string[]? AllowedCoreAssemblyVersions { get; set; }

    [DataMember(Name = "coreAssemblyVersionMin")]
    public string? CoreAssemblyVersionMin { get; set; }

    [DataMember(Name = "coreAssemblyVersionMax")]
    public string? CoreAssemblyVersionMax { get; set; }
}

[DataContract]
internal sealed class RuntimeLocalizationEntryContract
{
    [DataMember(Name = "id")]
    public string? Id { get; set; }

    [DataMember(Name = "sourceText")]
    public string? SourceText { get; set; }

    [DataMember(Name = "sourceTextBase64")]
    public string? SourceTextBase64 { get; set; }

    [DataMember(Name = "targetText")]
    public string? TargetText { get; set; }
}

