using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NetWasm.UI.Generator;

namespace NetWasm.UI.Tests;

public class DevStateGeneratorTests
{
    private const string DevReloadSource =
        "namespace NetWasm.UI.Browser { public static class DevReload { public static void Install() { } } }";

    [Theory]
    [InlineData("Debug", null, true)]
    [InlineData("debug", null, true)]
    [InlineData("Release", null, false)]
    [InlineData("Debug", "false", false)]
    [InlineData("Release", "true", true)]
    public void Module_initializer_only_in_debug_unless_overridden(string configuration, string? setting, bool expected)
    {
        var generated = Run(configuration, setting, referenceBrowser: true);

        Assert.Equal(expected, generated is not null);
        if (generated is not null)
        {
            Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", generated);
            Assert.Contains("global::NetWasm.UI.Browser.DevReload.Install()", generated);
        }
    }

    [Fact]
    public void Nothing_without_NetWasm_UI_Browser()
    {
        Assert.Null(Run("Debug", null, referenceBrowser: false));
    }

    [Fact]
    public void Nothing_inside_NetWasm_UI_Browser_itself()
    {
        Assert.Null(Run("Debug", null, referenceBrowser: false, source: DevReloadSource));
    }

    private static string? Run(string configuration, string? setting, bool referenceBrowser, string source = "class C { }")
    {
        var refs = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        if (referenceBrowser)
        {
            refs.Add(CSharpCompilation.Create(
                    "NetWasm.UI.Browser",
                    new[] { CSharpSyntaxTree.ParseText(DevReloadSource) },
                    refs,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var properties = new Dictionary<string, string> { ["build_property.Configuration"] = configuration };
        if (setting is not null)
        {
            properties["build_property.NetWasmUIDevState"] = setting;
        }

        var driver = CSharpGeneratorDriver.Create(
            new[] { new DevStateGenerator().AsSourceGenerator() },
            optionsProvider: new Options(properties));
        var run = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return run.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString();
    }

    private sealed class Options(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(global);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values(new());

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(new());
    }

    private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
