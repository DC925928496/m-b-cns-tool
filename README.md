# m&b-cns-tool

用于 `Mount & Blade II: Bannerlord` Mod 的 Windows 汉化生成工具。

## 功能概览

- 翻译引擎仅保留两种：`谷歌免费接口` 与 `自定义 OpenAI 兼容接口`。
- 自定义接口失败时自动回退到谷歌免费接口，保证流程不中断。
- 网络请求内置增强重试（429/5xx/超时/SSL），降低偶发网络错误影响。
- 自动识别文本类型（对话、物品、菜单、系统），按类型走翻译策略。
- 自动保护变量占位符（如 `{PLAYER_NAME}`、`<RANK>`）。
- 支持术语表统一（如 `Denar=第纳尔`）。
- 支持术语表 JSON 导入（自动去重）与模板文件。
- 运行时自动提取高频词，先调用翻译引擎生成中文术语后再追加到术语表（英英结果会自动跳过）。
- 支持 DLL 硬编码字符串扫描，生成 `runtime_localization.json` 映射。
- 文本翻译与 DLL 翻译支持受控并发，默认并发数为 `6`。
- 支持两种输出模式：
  - `external`：生成独立 `*_CNs` 汉化包。
  - `overlay`：直接覆盖原 Mod 文件。
- 内置 SQLite 缓存，重复文本不重复翻译。

## 目录结构

- `MbCnsTool.Core`：核心流程与翻译引擎。
- `MbCnsTool.Cli`：命令行入口。
- `MbCnsTool.Wpf`：Windows 图形界面入口。
- `MbCnsTool.Tests`：单元测试。
- `glossary/default_glossary.txt`：默认术语表示例。
- `glossary/glossary.template.json`：术语导入模板 JSON。
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
dotnet run --project MbCnsTool.Cli\MbCnsTool.Cli.csproj -c Release -- `
  --mod "D:\Bannerlord\Modules\Enlisted" `
  --output ".\artifacts" `
  --mode external `
  --style "史诗叙事风" `
  --providers google_free,fallback `
  --glossary ".\glossary\default_glossary.txt"
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
  - 术语 JSON 模板：`glossary/glossary.template.json`
  - 缓存：`artifacts/cache/translation_cache.db`
- 目标语言固定为简体中文，不在界面展示。
- 输出模式使用中文选项与解释文案。
- 翻译引擎下拉仅保留 `谷歌免费接口` 与 `自定义 AI`。
- 支持在界面设置并发数（`1-32`，默认 `6`）。
- 自定义 AI 失败自动回退谷歌接口。
- 点击“开始汉化”后，提供 `扫描/文本翻译/DLL翻译/打包` 四段进度条和详细日志。
- 执行中可点击“中断任务”，中断后可点击“继续汉化”基于缓存断点继续。
- 默认翻译风格提示词为：`请用骑马与砍杀2的中世纪风格进行翻译`。
