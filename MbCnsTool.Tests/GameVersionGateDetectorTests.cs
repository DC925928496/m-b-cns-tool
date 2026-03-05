using MbCnsTool.Core.Services;
using Mono.Cecil;

namespace MbCnsTool.Tests;

/// <summary>
/// 游戏版本门禁探测器测试。
/// </summary>
public sealed class GameVersionGateDetectorTests
{
    [Fact]
    public void TryDetect_No_SubModuleXml_Should_Return_Null()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var detector = new GameVersionGateDetector();
            var gate = detector.TryDetect(root);
            Assert.Null(gate);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryDetect_Declared_Dll_With_TaleWorldsCore_Reference_Should_Return_Gate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gate-{Guid.NewGuid():N}");
        var bin = Path.Combine(root, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);

        var dllName = "DemoMod.dll";
        var dllPath = Path.Combine(bin, dllName);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "SubModule.xml"),
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <Module>
                  <SubModules>
                    <SubModule>
                      <DLLName value="{{dllName}}" />
                    </SubModule>
                  </SubModules>
                </Module>
                """);

            using (var assembly = AssemblyDefinition.CreateAssembly(
                       new AssemblyNameDefinition("DemoMod", new Version(1, 0, 0, 0)),
                       "DemoMod",
                       ModuleKind.Dll))
            {
                assembly.MainModule.AssemblyReferences.Add(
                    new AssemblyNameReference("TaleWorlds.Core", new Version(1, 2, 3, 0)));
                assembly.Write(dllPath);
            }

            var detector = new GameVersionGateDetector();
            var gate = detector.TryDetect(root);
            Assert.NotNull(gate);
            Assert.NotNull(gate!.AllowedCoreAssemblyVersions);
            Assert.Contains("1.2.3.0", gate.AllowedCoreAssemblyVersions!);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

