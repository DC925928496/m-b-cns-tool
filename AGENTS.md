# Repository Guidelines

## Project Structure & Module Organization
- `MbCnsTool.Core/`：核心汉化流程、提取、翻译编排、打包逻辑。
- `MbCnsTool.Cli/`：命令行入口，适合批处理与脚本化手工执行。
- `MbCnsTool.Wpf/`：Windows 图形界面入口（WPF）。
- `MbCnsTool.Tests/`：xUnit 单元测试，按核心服务与流程分文件维护。
- `glossary/`：默认术语表与 JSON 模板；`artifacts/`：本地运行与发布产物（不提交）。

## Build, Test, and Development Commands
- `dotnet build MbCnsTool.sln -c Release`：手工编译全部项目。
- `dotnet test MbCnsTool.sln -c Release --no-build`：运行全部测试。
- `dotnet test MbCnsTool.Tests/MbCnsTool.Tests.csproj --collect:"XPlat Code Coverage"`：采集覆盖率。
- `dotnet run --project MbCnsTool.Cli/MbCnsTool.Cli.csproj -c Release -- --mod "<Mod目录>" --output "./artifacts"`：本地运行 CLI。
- `dotnet publish MbCnsTool.Wpf/MbCnsTool.Wpf.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish-wpf-portable`：发布便携版 GUI。
- 禁用 CI/CD；构建、测试、发布均为人工执行并记录结果。

## Coding Style & Naming Conventions
- 语言与框架：C# / .NET 8（`Nullable` 与 `ImplicitUsings` 已启用）。
- 缩进 4 空格，类型/文件名使用 `PascalCase`，局部变量使用 `camelCase`。
- 异步方法必须使用 `Async` 后缀；测试命名采用 `Method_Scenario_Should_Result`。
- 注释与文档使用中文，优先 `///` XML 注释；禁止保留过时代码，默认破坏性变更。

## Testing Guidelines
- 测试框架：xUnit + `Microsoft.NET.Test.Sdk` + `coverlet.collector`。
- 新增功能必须覆盖正常路径、异常分支、回归场景；总体覆盖率目标不低于 90%。
- 测试文件放在 `MbCnsTool.Tests/`，与被测模块保持可追踪映射关系。

## Commit & Pull Request Guidelines
- 当前历史较少（如 `Initial commit`、`更新readme`）；后续提交请使用简短祈使句并标明范围，例如：`core: 优化 DLL 字符串过滤规则`。
- 单次提交只做一类变更（功能/重构/测试分离），提交前必须本地通过构建与测试。
- PR 必须包含：变更说明、手工验证步骤与结果、风险点、关联任务；涉及 WPF 界面变更时附截图。

## Security & Configuration Tips
- 不提交任何密钥、令牌或内部链接；自定义翻译接口配置仅存放在本地环境。
- 严禁提交 `artifacts/`、`bin/`、`obj/` 产物目录。
- 迁移策略：无迁移，直接替换。
