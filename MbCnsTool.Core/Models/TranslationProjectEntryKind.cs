namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译工程条目类型。
/// </summary>
public enum TranslationProjectEntryKind
{
    /// <summary>
    /// 语言文件中的 <c>&lt;string id="..." text="..." /&gt;</c> 条目。
    /// </summary>
    LanguageString = 0,

    /// <summary>
    /// 从非语言 XML/JSON 中收集到的 <c>{=id}</c> 引用，且语言文件缺失该 id。
    /// </summary>
    MissingIdFromReferences = 1,

    /// <summary>
    /// DLL 中 TextObject 构造参数为 <c>{=id}默认文本</c>，且语言文件缺失该 id。
    /// </summary>
    DllTextObjectIdMissing = 2,

    /// <summary>
    /// DLL 中 TextObject 构造参数为硬编码文本（不含 <c>{=id}</c> 前缀）。
    /// </summary>
    DllTextObjectHardcoded = 3
}

