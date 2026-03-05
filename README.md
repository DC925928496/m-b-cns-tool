# m-b-cns-tool

`Mount & Blade II: Bannerlord` Mod 汉化工具（WPF + CLI）。

## 核心流程（与骑砍2本地化规范对齐）

1. 选择需要汉化的 Mod 根目录（可传父目录，工具会自动定位 `SubModule.xml` 所在模块根）
2. 可选：扫描 DLL（严格限定为 `TextObject` 构造函数首参为字符串字面量的场景）
3. 扫描并生成“工程记录”（JSON）：按 **所属文件 / id / text / 译文** 展示与编辑
4. “开始翻译”：使用谷歌翻译填充空译文（并强制占位符安全校验）
5. 人工校对并保存工程记录
6. “打包”：生成依赖原 Mod 的外挂汉化 Mod（外置包），必要时包含运行时注入 DLL
7. 再次处理同一 Mod：优先复用已保存的工程记录

## 关键特性

- 语言文件扫描：遍历 `ModuleData/Languages/` 下全部 `*.xml`，并优先使用目标语言目录（如 `CNs`）的 `language_data.xml` 定义。
- DLL 扫描：只收集 `new TextObject("...")` 这类字符串字面量；`{=id}` 若已在语言 XML 中存在对应 `<string id="id">` 则去重不入列表。
- 硬编码文本：进入“运行时注入”路径，生成 `runtime_localization.json`（数组结构，带 `sourceTextBase64`，兼容换行/转义差异）。
- 严格安全优先：
  - 占位符 `{...}` / `{{...}}` / `<...>` / `%...%` 必须保持一致，不安全译文会被回退/忽略；
  - 运行时注入带版本门禁：无法判定版本或不在允许范围时，注入完全禁用（不崩溃、不修改入参）。

## 项目结构

- `MbCnsTool.Core/`：核心流程（扫描、翻译编排、打包、记录）
- `MbCnsTool.Cli/`：命令行入口（`scan/translate/package`）
- `MbCnsTool.Wpf/`：Windows 图形界面入口（DataGrid 内联校对）
- `MbCnsTool.RuntimeLocalization/`：运行时注入子模块（Harmony + 版本门禁）
- `MbCnsTool.Tests/`：xUnit 单元测试
- `glossary/`：默认术语表
- `artifacts/`：本地运行与发布产物（不提交）

## 手工构建与测试

```powershell
dotnet build MbCnsTool.sln -c Release
dotnet test MbCnsTool.sln -c Release --no-build
```

## CLI 用法

```powershell
# 1) 扫描：生成/更新工程记录
dotnet run --project MbCnsTool.Cli/MbCnsTool.Cli.csproj -c Release -- `
  scan --mod "D:\Bannerlord\Modules\SomeMod" --scan-dll true

# 2) 机翻：填充空译文，并写回工程记录
dotnet run --project MbCnsTool.Cli/MbCnsTool.Cli.csproj -c Release -- `
  translate --mod "D:\Bannerlord\Modules\SomeMod" --scan-dll true

# 3) 打包：读取工程记录 + 缓存，生成外挂汉化 Mod
dotnet run --project MbCnsTool.Cli/MbCnsTool.Cli.csproj -c Release -- `
  package --mod "D:\Bannerlord\Modules\SomeMod" --mode external --scan-dll true
```

常用参数：

- `--mod`：Mod 路径（模块根或其父目录）
- `--output`：外挂汉化 Mod 输出目录（仅 `package --mode external` 使用；默认输出到原 Mod 同级目录）
- `--scan-dll`：是否扫描 DLL（默认 `false`）
- `--project`：工程记录路径（默认：`<工具目录>/data/records/<模块名>.mbcns_project.json`）
- `--cache`：缓存数据库路径（默认：`<工具目录>/data/cache/translation_cache.db`）
- `--mode`：`external` 或 `overlay`（注意：`overlay` 不支持运行时注入条目）

持久化数据说明：

- 工程记录与缓存默认持久化在工具运行目录（`AppContext.BaseDirectory`）下的 `data/` 目录，与 `--output` 无关。

## WPF 用法

发布/运行（示例）：

```powershell
dotnet publish MbCnsTool.Wpf/MbCnsTool.Wpf.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish-wpf
artifacts/publish-wpf/MbCnsTool.Wpf.exe
```

界面流程：

- 选择 Mod 根目录（外挂汉化包输出目录会自动填充为“原 Mod 同级”，也可手工自定义）
- 勾选是否扫描 DLL
- 点击“扫描”加载条目到表格
- 点击“开始翻译”自动填充空译文
- 在表格中直接编辑“译文”，点击“保存记录”
- 点击“打包”生成外挂汉化 Mod

DLL 扫描提示：

- 必须确保 Mod 目录下存在 `bin/**/<DLLName>.dll`（`<DLLName>` 来自 `SubModule.xml` 的 `<DLLName value="..."/>`）。
- 若你选择的是源码仓库/未编译版本，通常没有 `bin/`，扫描会提示“未找到目录/未找到声明 DLL”，并返回 `DLL 字面量 0`（属正常行为）。

## 运行时注入 DLL 的放置

工具在打包时需要复制 `MbCnsTool.RuntimeLocalization.dll` 与 `0Harmony.dll` 到外挂包的 `bin/Win64_Shipping_Client/`。

默认查找顺序：

1. 环境变量 `MBCNS_RUNTIME_INJECTOR_DIR` 指向的目录（若设置）
2. 工具运行目录（`AppContext.BaseDirectory`）及其 `runtime_injector/` 子目录
3. 源码构建目录的 `MbCnsTool.RuntimeLocalization/bin/(Release|Debug)/net472/`（便于从源码运行）

## 迁移策略

无迁移，直接替换。
