using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.UI.Workers.Generator;

namespace NetWasm.UI.Tests;

public class WorkerGeneratorTests
{
    private static readonly CSharpParseOptions Parse = CSharpParseOptions.Default
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "NetWasm.UI.Generated") });

    // The runtime targets net10.0-netwasm, so the tests declare the shapes the generator looks for.
    private const string Stubs = """
        namespace NetWasm.UI.Workers
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class WorkerContractAttribute : System.Attribute
            {
                public string? Worker { get; set; }
                public System.Type? JsonContext { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WorkerServiceAttribute : System.Attribute { }

            public static class WorkerServices
            {
                public static NetWasm.UI.Services AddWorker<T>(this NetWasm.UI.Services services, string? worker = null, bool terminateOnCancel = false) where T : class => services;
                public static NetWasm.UI.Services AddWorkerPool<T>(this NetWasm.UI.Services services, int size = 0, string? worker = null, bool terminateOnCancel = false) where T : class => services;
            }
        }
        """;

    private const string Contract = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using NetWasm.UI.Workers;

        namespace App;

        [WorkerContract(Worker = "App.Worker")]
        public interface ICalc
        {
            Task<int> AddAsync(int a, int b, CancellationToken ct = default);
            Task<string> EchoAsync(string text, System.IProgress<int>? progress = null);
            IAsyncEnumerable<int> CountAsync(int from, CancellationToken ct = default);
            Task ResetAsync();
        }
        """;

    [Fact]
    public void AddWorker_is_intercepted_with_a_proxy_for_the_contract()
    {
        var (generated, diagnostics) = Run(Contract + """

            public static class Setup
            {
                public static NetWasm.UI.Services Create() => new NetWasm.UI.Services().AddWorker<ICalc>();
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, ", generated);
        Assert.Contains("class WorkerProxy_App_ICalc : global::App.ICalc, global::System.IDisposable", generated);
        Assert.Contains("\"App.Worker\"", generated);
        Assert.Contains("CountAsync", generated);
    }

    [Fact]
    public void A_worker_service_gets_a_JSExport_per_contract_method()
    {
        var (generated, diagnostics) = Run(Contract + """

            [WorkerService]
            public sealed class Calc : ICalc
            {
                public Task<int> AddAsync(int a, int b, CancellationToken ct = default) => Task.FromResult(a + b);
                public Task<string> EchoAsync(string text, System.IProgress<int>? progress = null) => Task.FromResult(text);
                public async IAsyncEnumerable<int> CountAsync(int from, CancellationToken ct = default) { yield return from; await Task.Yield(); }
                public Task ResetAsync() => Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("[global::System.Runtime.InteropServices.JavaScript.JSExport(\"App_ICalc_AddAsync\")]", generated);
        Assert.Contains("[global::System.Runtime.InteropServices.JavaScript.JSExport(\"App_ICalc_ResetAsync\")]", generated);
        Assert.Contains("__nwui_cancel", generated);
    }

    [Theory]
    [InlineData("Task<int> A(int x); Task<int> A(string x);", "overload")]
    [InlineData("int Sync();", "must return Task")]
    [InlineData("Task Out(out int x);", "ref")]
    [InlineData("Task<System.DateTimeOffset> When();", "JsonContext")]
    public void Invalid_contracts_report_NWUI101(string members, string message)
    {
        var (_, diagnostics) = Run($$"""
            using System.Threading.Tasks;
            using NetWasm.UI.Workers;

            [WorkerContract(Worker = "W")]
            public interface IBad { {{members}} }

            public static class Setup
            {
                public static NetWasm.UI.Services Create() => new NetWasm.UI.Services().AddWorker<IBad>();
            }
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal("NWUI101", d.Id);
        Assert.Contains(message, d.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_contract_without_a_worker_name_reports_NWUI102_unless_one_is_passed()
    {
        const string Source = """
            using System.Threading.Tasks;
            using NetWasm.UI.Workers;

            [WorkerContract]
            public interface INoName { Task Run(); }

            public static class Setup
            {
                public static NetWasm.UI.Services A() => new NetWasm.UI.Services().AddWorker<INoName>();
                public static NetWasm.UI.Services B() => new NetWasm.UI.Services().AddWorkerPool<INoName>(worker: "W");
            }
            """;

        var d = Assert.Single(Run(Source).Diagnostics);
        Assert.Equal("NWUI102", d.Id);
        Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void A_worker_service_without_a_contract_reports_NWUI103()
    {
        var (_, diagnostics) = Run("""
            [NetWasm.UI.Workers.WorkerService]
            public sealed class Lonely { }
            """);

        Assert.Equal("NWUI103", Assert.Single(diagnostics).Id);
    }

    private static (string Generated, IReadOnlyList<Diagnostic> Diagnostics) Run(string source)
    {
        var refs = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(Component).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "T",
            new[] { CSharpSyntaxTree.ParseText(source, Parse, path: "C:\\p\\Setup.cs"), CSharpSyntaxTree.ParseText(Stubs, Parse, path: "C:\\p\\Stubs.cs") },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create(new[] { new WorkerGenerator().AsSourceGenerator() }, parseOptions: Parse);
        var run = driver.RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", run.GeneratedTrees.Select(t => t.ToString())), run.Diagnostics);
    }
}
