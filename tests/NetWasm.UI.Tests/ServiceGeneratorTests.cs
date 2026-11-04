using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.UI.Generator;

namespace NetWasm.UI.Tests;

public class ServiceGeneratorTests
{
    private static readonly CSharpParseOptions Parse = CSharpParseOptions.Default
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "NetWasm.UI.Generated") });

    [Fact]
    public void Constructor_parameters_become_Inject_calls()
    {
        var (generated, diagnostics, errors) = Run("""
            namespace App;
            public interface IClock { }
            public interface IApi { }
            internal sealed class Api(IClock clock, System.Net.Http.HttpClient http) : IApi { }
            internal sealed class Clock : IClock { }
            public static class Setup
            {
                public static NetWasm.UI.Services Create() =>
                    new NetWasm.UI.Services().AddLazy<IApi, Api>().AddLazy<IClock, Clock>().AddLazy<Clock>();
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Contains(
            "services.AddLazy<global::App.IApi>(static () => new global::App.Api(global::NetWasm.UI.Html.Inject<global::App.IClock>(), global::NetWasm.UI.Html.Inject<global::System.Net.Http.HttpClient>()));",
            generated);
        Assert.Contains("services.AddLazy<global::App.IClock>(static () => new global::App.Clock());", generated);
        Assert.Contains("services.AddLazy<global::App.Clock>(static () => new global::App.Clock());", generated);
    }

    [Theory]
    [InlineData("public abstract class Svc { }", "non-abstract class")]
    [InlineData("public sealed class Svc { public Svc() { } public Svc(int x) { } }", "more than one accessible constructor")]
    [InlineData("public sealed class Svc { private Svc() { } }", "no public or internal constructor")]
    [InlineData("public sealed class Svc { public Svc(ref int x) { } }", "ref, out, in or params")]
    [InlineData("public sealed class Svc { public Svc(params int[] x) { } }", "ref, out, in or params")]
    public void Unconstructable_services_report_NWUI010(string type, string message)
    {
        var (_, diagnostics, _) = Run($$"""
            {{type}}
            public static class Setup
            {
                public static NetWasm.UI.Services Create() => new NetWasm.UI.Services().AddLazy<Svc>();
            }
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal("NWUI010", d.Id);
        Assert.Contains(message, d.GetMessage());
    }

    [Fact]
    public void Private_nested_services_report_NWUI010()
    {
        var (_, diagnostics, _) = Run("""
            public static class Setup
            {
                private sealed class Hidden { }
                public static NetWasm.UI.Services Create() => new NetWasm.UI.Services().AddLazy<Hidden>();
            }
            """);

        Assert.Contains("public or internal", Assert.Single(diagnostics).GetMessage());
    }

    [Fact]
    public void Factory_overloads_and_other_AddLazy_methods_are_ignored()
    {
        var (generated, diagnostics, errors) = Run("""
            public sealed class Svc { }
            public sealed class Other { public Other AddLazy<T>() => this; }
            public static class Setup
            {
                public static NetWasm.UI.Services Create()
                {
                    new Other().AddLazy<Svc>();
                    return new NetWasm.UI.Services().AddLazy(() => new Svc());
                }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(errors);
        Assert.Null(generated);
    }

    private static (string? Generated, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<Diagnostic> Errors) Run(string source)
    {
        var refs = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(Component).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "T",
            new[] { CSharpSyntaxTree.ParseText(source, Parse, path: "C:\\p\\Setup.cs") },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            new[] { new ServiceGenerator().AsSourceGenerator() },
            parseOptions: Parse);

        var run = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generated = run.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString();
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (generated, diagnostics, errors);
    }
}