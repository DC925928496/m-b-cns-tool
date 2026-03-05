namespace MbCnsTool.Core.Models;

/// <summary>
/// 条目上下文（仅用于 DLL 扫描结果的审计与定位）。
/// </summary>
public sealed record TranslationProjectEntryContext(
    string AssemblyName,
    string TypeName,
    string MethodName
);

