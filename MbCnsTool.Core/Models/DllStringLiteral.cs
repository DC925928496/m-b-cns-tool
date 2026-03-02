namespace MbCnsTool.Core.Models;

/// <summary>
/// DLL 中抽取出的硬编码字符串。
/// </summary>
public sealed record DllStringLiteral(
    string AssemblyName,
    string TypeName,
    string MethodName,
    string SourceText
);
