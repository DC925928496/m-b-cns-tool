# m&b-cns-tool

用于 `Mount & Blade II: Bannerlord` Mod 的 Windows 汉化生成工具。

## 功能概览

- 翻译引擎仅保留两种：`谷歌免费接口` 与 `自定义 OpenAI 兼容接口`。
- 自定义接口失败时自动回退到谷歌免费接口，保证流程不中断。
- 网络请求内置增强重试（429/5xx/超时/SSL），降低偶发网络错误影响。
- 自动识别文本类型（对话、物品、菜单、系统），按类型走翻译策略。
- 自动保护变量占位符（如 `{PLAYER_NAME}`、`<RANK>`）。
- 支持术语表统一（如 `Denar=第纳尔`）。
- 流程改为两阶段：先翻译并生成 `translation_review.json`，人工确认后再执行最终打包。
- GUI 提供翻译对比可视化编辑窗口，可直接修改缓存译文。
- 支持从 XML/JSON/DLL 中收集 `{=id}` 本地化引用，并自动补全到 `std_module_strings_xml.xml`（严格按骑砍2本地化规范）。
- 文本翻译支持受控并发，默认并发数为 `6`。
- 支持两种输出模式：
  - `external`：生成独立 `*_CNs` 汉化包。
  - `overlay`：直接覆盖原 Mod 文件。
- `external` 模式会优先生成 `Language-CNs` 语言包（接口文本），并将无接口文本输出为同路径 `*.xslt` 补丁，避免无差别整包覆盖。
- 内置 SQLite 缓存，重复文本不重复翻译。

## 目录结构

- `MbCnsTool.Core`：核心流程与翻译引擎。
- `MbCnsTool.Cli`：命令行入口。
- `MbCnsTool.Wpf`：Windows 图形界面入口。
- `MbCnsTool.Tests`：单元测试。
- `glossary/default_glossary.txt`：默认术语表示例。
- `artifacts/`：运行与发布产物目录（默认不提交仓库）。

## 手工构建

```powershell
dotnet build MbCnsTool.sln -c Release
dotnet test MbCnsTool.sln -c Release --no-build
dotnet publish MbCnsTool.Cli\MbCnsTool.Cli.csproj -c Release -r win-x64 --self-contained false -o artifacts\publish
dotnet publish MbCnsTool.Wpf\MbCnsTool.Wpf.csproj -c Release -r win-x64 --self-contained false -o artifacts\publish-wpf
```

## 便携包发布（方案1）

```powershell
dotnet build MbCnsTool.sln -c Release
dotnet publish MbCnsTool.Wpf\MbCnsTool.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o artifacts\publish-wpf-portable
Compress-Archive -Path artifacts\publish-wpf-portable\* -DestinationPath artifacts\MbCnsTool.Wpf-win-x64-portable.zip -Force
```

- 对外分发文件：`artifacts/MbCnsTool.Wpf-win-x64-portable.zip`
- 用户使用方式：解压后直接运行 `MbCnsTool.Wpf.exe`

## 仓库清理

```powershell
dotnet clean MbCnsTool.sln
```

- 运行/发布生成目录（如 `artifacts/`、各项目 `bin/`、`obj/`）已加入 `.gitignore`。

## 运行示例

```powershell
# 第一步：翻译并生成可编辑对比文件（不打包）
dotnet run --project MbCnsTool.Cli\MbCnsTool.Cli.csproj -c Release -- `
  --mod "D:\Bannerlord\Modules\Enlisted" `
  --output ".\artifacts" `
  --mode external `
  --style "史诗叙事风" `
  --providers google_free,fallback `
  --glossary ".\glossary\default_glossary.txt"

# 第二步：手动确认对比文件后执行最终打包
dotnet run --project MbCnsTool.Cli\MbCnsTool.Cli.csproj -c Release -- `
  --mod "D:\Bannerlord\Modules\Enlisted" `
  --output ".\artifacts" `
  --mode external `
  --style "史诗叙事风" `
  --providers google_free,fallback `
  --glossary ".\glossary\default_glossary.txt" `
  --finalize true
```

## 参数说明

- `--mod`：Mod 路径（可传父目录，工具会自动定位 `SubModule.xml`）。
- `--output`：输出根目录，默认 `./artifacts`。
- `--mode`：`external` 或 `overlay`。
- `--style`：翻译风格，例如 `官方直译`、`武侠风`、`史诗叙事风`。
- `--providers`：翻译链路，默认 `google_free,fallback`。
- `--concurrency`：翻译并发数，默认 `6`，建议范围 `1-32`。
- `--glossary`：术语表文件路径。
- `--cache`：缓存数据库路径，默认 `artifacts/cache/translation_cache.db`。
- `--review`：翻译对比文件路径（JSON），默认 `artifacts/review/<模块名>.translation_review.json`。
- `--finalize`：`true/false`，`true` 表示执行最终打包阶段；默认不打包，仅生成翻译对比数据。
- `--target`：目标语言，默认 `zh-CN`。

## 开源协议

本项目采用 `MIT License` 开源协议。

## 迁移策略

无迁移，直接替换。

## GUI 启动（具体用法）

- 环境要求：Windows（使用便携包方案时无需额外安装 .NET Runtime）。

- 第一次使用（推荐）：

```powershell
dotnet build MbCnsTool.sln -c Release
dotnet publish MbCnsTool.Wpf\MbCnsTool.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o artifacts\publish-wpf-portable
```

- 启动方式（命令行）：

```powershell
artifacts\publish-wpf-portable\MbCnsTool.Wpf.exe
```

## GUI 说明（当前版本）

- 界面采用极简风布局，分为参数区、四段进度区、日志区。
- 主窗口支持自适应拉伸（含最小尺寸约束），日志区会随窗口放大。
- 固定路径：
  - 术语表：`glossary/default_glossary.txt`
  - 缓存：`artifacts/cache/translation_cache.db`
- 目标语言固定为简体中文，不在界面展示。
- 输出模式使用中文选项与解释文案。
- 翻译引擎下拉仅保留 `谷歌免费接口` 与 `自定义 AI`。
- 支持在界面设置并发数（`1-32`，默认 `6`）。
- 自定义 AI 失败自动回退谷歌接口。
- 点击“开始翻译”后，先执行 `扫描/文本翻译` 并生成翻译对比文件。
- 可通过“编辑翻译对比”可视化修改译文，保存后自动写回缓存。
- 确认无误后点击“最终打包”执行最后一步。
- 默认翻译风格提示词为：`请用骑马与砍杀2的中世纪风格进行翻译`。
