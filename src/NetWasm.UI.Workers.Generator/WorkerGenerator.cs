using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetWasm.UI.Workers.Generator;

/// <summary>
/// Writes both sides of <c>[WorkerContract]</c> interfaces: in the app, rewrites <c>AddWorker&lt;T&gt;()</c> and
/// <c>AddWorkerPool&lt;T&gt;()</c> with C# interceptors into a proxy that calls the worker; in the worker, exports each
/// method of a <c>[WorkerService]</c> class with <c>[JSExport]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WorkerGenerator : IIncrementalGenerator
{
    public static readonly DiagnosticDescriptor ContractError = new(
        "NWUI101", "Invalid worker contract", "{0}", "NetWasm.UI.Workers", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor WorkerNameMissing = new(
        "NWUI102", "Worker name missing", "{0}", "NetWasm.UI.Workers", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ServiceError = new(
        "NWUI103", "Invalid worker service", "{0}", "NetWasm.UI.Workers", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private const string Ns = "global::NetWasm.UI.Workers.";
    private const string ServiceAttribute = "NetWasm.UI.Workers.WorkerServiceAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var calls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "AddWorker" or "AddWorkerPool", TypeArgumentList.Arguments.Count: 1 } },
                },
                static (ctx, ct) => AnalyzeCall(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(calls.Where(static c => c.Error is not null), static (spc, c) =>
            spc.ReportDiagnostic(Diagnostic.Create(c.Descriptor!, c.Location, c.Error)));

        context.RegisterSourceOutput(calls.Where(static c => c.Error is null).Collect(), static (spc, all) =>
        {
            if (!all.IsEmpty)
            {
                spc.AddSource("NetWasm.UI.Workers.Registrations.g.cs", EmitRegistrations(all));
            }
        });

        var services = context.SyntaxProvider.ForAttributeWithMetadataName(
            ServiceAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeService(ctx));

        context.RegisterSourceOutput(services.Where(static s => s.Error is not null), static (spc, s) =>
            spc.ReportDiagnostic(Diagnostic.Create(ServiceError, s.Location, s.Error)));

        context.RegisterSourceOutput(services.Where(static s => s.Error is null).Collect(), static (spc, all) =>
        {
            foreach (var s in all)
            {
                spc.AddSource(s.HintName + ".g.cs", s.Source);
            }

            if (!all.IsEmpty)
            {
                spc.AddSource("NetWasm.UI.Workers.Control.g.cs", EmitControl());
            }
        });
    }

    // App side ---------------------------------------------------------------------------------------------------

    private static Registration? AnalyzeCall(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
            || method.ContainingType is not { Name: "WorkerServices" } owner
            || owner.ContainingNamespace?.ToDisplayString() != "NetWasm.UI.Workers")
        {
            return null;
        }

        var location = ((MemberAccessExpressionSyntax)invocation.Expression).Name.GetLocation();
        if (method.TypeArguments[0] is not INamedTypeSymbol type || !Contract.IsContract(type))
        {
            var name = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return Registration.Failed(ContractError, location, $"'{name}' must be an interface marked [WorkerContract] to be registered with {method.Name}<…>().");
        }

        var contract = Contract.Read(type);
        if (contract.Error is not null)
        {
            return Registration.Failed(ContractError, location, contract.Error);
        }

        var hasWorker = false;
        var parameters = method.ReducedFrom is null ? method.Parameters.Skip(1).ToImmutableArray() : method.Parameters;
        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i].NameColon?.Name.Identifier.ValueText ?? (i < parameters.Length ? parameters[i].Name : null);
            hasWorker |= name == "worker";
        }

        if (!hasWorker && contract.Worker is null)
        {
            var name = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return Registration.Failed(WorkerNameMissing, location, $"Which worker runs '{name}'? Set [WorkerContract(Worker = \"...\")] or pass worker: \"...\" to {method.Name}<…>().");
        }

#pragma warning disable RSEXPERIMENTAL002
        var interceptable = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptable is null)
        {
            return null;
        }

        var attribute = interceptable.GetInterceptsLocationAttributeSyntax();
#pragma warning restore RSEXPERIMENTAL002
        var proxy = "WorkerProxy_" + Sanitize(type.ToDisplayString());
        return new Registration(attribute, method.Name == "AddWorkerPool", contract.Type, contract.Worker, proxy, EmitProxy(contract, proxy), null, null, null);
    }

    private static string EmitRegistrations(ImmutableArray<Registration> all)
    {
        var b = new StringBuilder();
        b.Append("// <auto-generated/>\n#nullable enable\n\nnamespace NetWasm.UI.Generated\n{\n");
        b.Append("    internal static class WorkerRegistrations\n    {\n");
        var i = 0;
        foreach (var group in all.GroupBy(static r => (r.Pool, r.Contract, r.Worker, r.Proxy)).OrderBy(static g => g.Key.Contract + "|" + g.Key.Pool, System.StringComparer.Ordinal))
        {
            foreach (var attribute in group.Select(static r => r.Attribute).OrderBy(static a => a, System.StringComparer.Ordinal))
            {
                b.Append("        ").Append(attribute).Append('\n');
            }

            var (pool, contract, worker, proxy) = group.Key;
            var name = worker is null ? "worker" : "worker ?? " + Literal(worker);
            if (pool)
            {
                b.Append("        public static global::NetWasm.UI.Services AddWorkerPool").Append(i++)
                    .Append("(this global::NetWasm.UI.Services services, int size = 0, string? worker = null, bool terminateOnCancel = false) =>\n")
                    .Append("            services.AddLazy<").Append(contract).Append(">(() => new ").Append(proxy)
                    .Append('(').Append(Ns).Append("WorkerChannel.Pool(").Append(name).Append(", size, terminateOnCancel)));\n\n");
            }
            else
            {
                b.Append("        public static global::NetWasm.UI.Services AddWorker").Append(i++)
                    .Append("(this global::NetWasm.UI.Services services, string? worker = null, bool terminateOnCancel = false) =>\n")
                    .Append("            services.AddLazy<").Append(contract).Append(">(() => new ").Append(proxy)
                    .Append('(').Append(Ns).Append("WorkerChannel.Dedicated(").Append(name).Append(", terminateOnCancel)));\n\n");
            }
        }

        b.Append("    }\n");
        foreach (var proxy in all.GroupBy(static r => r.Proxy).Select(static g => g.First()).OrderBy(static r => r.Proxy, System.StringComparer.Ordinal))
        {
            b.Append('\n').Append(proxy.ProxySource);
        }

        b.Append("}\n\n");
        b.Append("namespace System.Runtime.CompilerServices\n{\n");
        b.Append("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]\n");
        b.Append("    file sealed class InterceptsLocationAttribute : global::System.Attribute\n    {\n");
        b.Append("        public InterceptsLocationAttribute(int version, string data)\n        {\n        }\n    }\n}\n");
        return b.ToString();
    }

    private static string EmitProxy(Contract contract, string proxy)
    {
        var b = new StringBuilder();
        b.Append("    file sealed class ").Append(proxy).Append(" : ").Append(contract.Type).Append(", global::System.IDisposable\n    {\n");
        b.Append("        private readonly ").Append(Ns).Append("WorkerChannel __channel;\n\n");
        b.Append("        public ").Append(proxy).Append('(').Append(Ns).Append("WorkerChannel channel) => __channel = channel;\n\n");
        b.Append("        public void Dispose() => __channel.Dispose();\n");

        foreach (var m in contract.Methods)
        {
            b.Append('\n');
            var signature = string.Join(", ", m.Params.Select(static p => p.Type + " " + p.Name));
            var json = new StringBuilder();
            var stage = new StringBuilder();
            var indent = m.Shape == Shape.Stream ? "                " : "            ";
            foreach (var p in m.Params)
            {
                switch (p.Role)
                {
                    case Role.Token:
                        break;
                    case Role.Progress:
                        stage.Append(indent).Append("__call.OnProgress<").Append(p.Value!.Type).Append(">(").Append(p.Name)
                            .Append(", static () => ").Append(Take(contract, p.Value)).Append(");\n");
                        stage.Append(indent).Append("__call.ArgBool(").Append(p.Name).Append(" is not null);\n");
                        break;
                    default:
                        var v = p.Value!;
                        if (v.Kind == Kind.Json)
                        {
                            json.Append("            var __json_").Append(p.Name.TrimStart('@')).Append(" = ").Append(Ns).Append("WorkerJson.Serialize<")
                                .Append(v.Type).Append(">(").Append(p.Name).Append(", ").Append(contract.TypeInfo(v.Type)).Append(");\n");
                            stage.Append(indent).Append("__call.ArgStr(__json_").Append(p.Name.TrimStart('@')).Append(");\n");
                        }
                        else
                        {
                            stage.Append(indent).Append("__call.Arg").Append(v.Suffix).Append('(').Append(p.Name).Append(");\n");
                        }

                        break;
                }
            }

            if (m.Shape == Shape.Stream)
            {
                b.Append("        public ").Append(m.ReturnType).Append(' ').Append(m.Name).Append('(').Append(signature).Append(")\n        {\n");
                b.Append(json);
                b.Append("            return __channel.Stream<").Append(m.Result.Type).Append(">(__call =>\n            {\n");
                b.Append(stage);
                b.Append("                return __call.InvokeVoid(").Append(Literal(m.Export)).Append(");\n");
                b.Append("            }, static () => ").Append(Take(contract, m.Result)).Append(", ").Append(m.Token).Append(");\n        }\n");
                continue;
            }

            b.Append("        public async ").Append(m.ReturnType).Append(' ').Append(m.Name).Append('(').Append(signature).Append(")\n        {\n");
            b.Append(json);
            b.Append("            var __call = await __channel.BeginAsync(").Append(m.Token).Append(").ConfigureAwait(false);\n");
            b.Append(stage);
            var invoke = "__call.Invoke" + (m.Result.Kind == Kind.Void ? "Void" : m.Result.Suffix) + "(" + Literal(m.Export) + ")";
            var awaited = "await __call.Complete(" + invoke + ", " + m.Token + ").ConfigureAwait(false)";
            switch (m.Result.Kind)
            {
                case Kind.Void:
                    b.Append("            ").Append(awaited).Append(";\n");
                    break;
                case Kind.Json:
                    b.Append("            return ").Append(Ns).Append("WorkerJson.Deserialize<").Append(m.Result.Type).Append(">(").Append(awaited)
                        .Append(", ").Append(contract.TypeInfo(m.Result.Type)).Append(");\n");
                    break;
                default:
                    b.Append("            return ").Append(awaited).Append(IsReference(m.Result.Kind) ? "!" : string.Empty).Append(";\n");
                    break;
            }

            b.Append("        }\n");
        }

        b.Append("    }\n");
        return b.ToString();
    }

    private static string Take(Contract contract, Value v) => v.Kind == Kind.Json
        ? Ns + "WorkerJson.Deserialize<" + v.Type + ">(" + Ns + "WorkerCall.TakeStr(), " + contract.TypeInfo(v.Type) + ")"
        : Ns + "WorkerCall.Take" + v.Suffix + "()" + (IsReference(v.Kind) ? "!" : string.Empty);

    // Worker side ------------------------------------------------------------------------------------------------

    private static ServiceExports AnalyzeService(GeneratorAttributeSyntaxContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        var location = ((ClassDeclarationSyntax)ctx.TargetNode).Identifier.GetLocation();
        var name = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var compilation = ctx.SemanticModel.Compilation;
        if (type.IsAbstract || type.IsStatic || type.IsGenericType)
        {
            return ServiceExports.Failed(location, $"Worker service '{name}' must be a non-abstract, non-generic class.");
        }

        if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)
            || !type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && compilation.IsSymbolAccessibleWithin(c, compilation.Assembly)))
        {
            return ServiceExports.Failed(location, $"Worker service '{name}' needs a public or internal parameterless constructor (and must not be a private nested class).");
        }

        var contracts = type.AllInterfaces.Where(Contract.IsContract).ToList();
        if (contracts.Count == 0)
        {
            return ServiceExports.Failed(location, $"Worker service '{name}' does not implement an interface marked [WorkerContract].");
        }

        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var holder = "WorkerExports_" + Sanitize(type.ToDisplayString());
        var b = new StringBuilder();
        b.Append("// <auto-generated/>\n#nullable enable\n#pragma warning disable CS1591\n\nnamespace NetWasm.UI.Generated\n{\n");
        b.Append("    internal static partial class ").Append(holder).Append("\n    {\n");
        b.Append("        private static ").Append(fq).Append("? __service;\n\n");
        b.Append("        private static ").Append(fq).Append(" Service => __service ??= new ").Append(fq).Append("();\n");

        foreach (var iface in contracts)
        {
            var contract = Contract.Read(iface);
            if (contract.Error is not null)
            {
                return ServiceExports.Failed(location, contract.Error);
            }

            foreach (var m in contract.Methods)
            {
                EmitExport(b, contract, m);
            }
        }

        b.Append("    }\n}\n");
        return new ServiceExports(holder, b.ToString(), null, null);
    }

    private static void EmitExport(StringBuilder b, Contract contract, Method m)
    {
        var wire = new StringBuilder("int __callId");
        var args = new StringBuilder();
        foreach (var p in m.Params)
        {
            if (args.Length > 0)
            {
                args.Append(", ");
            }

            switch (p.Role)
            {
                case Role.Token:
                    args.Append("__ct");
                    break;
                case Role.Progress:
                    wire.Append(", bool ").Append(p.Name);
                    args.Append(Ns).Append("WorkerCalls.Progress<").Append(p.Value!.Type).Append(">(__callId, ").Append(p.Name).Append(", ")
                        .Append(Notify(contract, p.Value)).Append(')');
                    break;
                default:
                    var v = p.Value!;
                    if (v.Kind == Kind.Json)
                    {
                        wire.Append(", string? ").Append(p.Name);
                        args.Append(Ns).Append("WorkerJson.Deserialize<").Append(v.Type).Append(">(").Append(p.Name).Append(", ")
                            .Append(contract.TypeInfo(v.Type)).Append(')');
                    }
                    else
                    {
                        wire.Append(", ").Append(v.Type).Append(' ').Append(p.Name);
                        args.Append(p.Name);
                    }

                    break;
            }
        }

        var call = "((" + contract.Type + ")Service)." + m.Name + "(" + args + ")";
        var returns = m.Shape == Shape.Stream || m.Result.Kind == Kind.Void
            ? "global::System.Threading.Tasks.Task"
            : "global::System.Threading.Tasks.Task<" + (m.Result.Kind == Kind.Json ? "string" : m.Result.Type) + ">";

        b.Append("\n        [global::System.Runtime.InteropServices.JavaScript.JSExport(").Append(Literal(m.Export)).Append(")]\n");
        b.Append("        public static async ").Append(returns).Append(' ').Append(m.Export).Append('(').Append(wire).Append(")\n        {\n");
        b.Append("            var __ct = ").Append(Ns).Append("WorkerCalls.Begin(__callId);\n");
        b.Append("            try\n            {\n");
        if (m.Shape == Shape.Stream)
        {
            b.Append("                await ").Append(Ns).Append("WorkerCalls.Stream<").Append(m.Result.Type).Append(">(__callId, ").Append(call).Append(", ")
                .Append(Notify(contract, m.Result)).Append(").ConfigureAwait(false);\n");
        }
        else if (m.Result.Kind == Kind.Void)
        {
            b.Append("                await ").Append(call).Append(".ConfigureAwait(false);\n");
        }
        else if (m.Result.Kind == Kind.Json)
        {
            b.Append("                return ").Append(Ns).Append("WorkerJson.Serialize<").Append(m.Result.Type).Append(">(await ").Append(call)
                .Append(".ConfigureAwait(false), ").Append(contract.TypeInfo(m.Result.Type)).Append(");\n");
        }
        else
        {
            b.Append("                return await ").Append(call).Append(".ConfigureAwait(false);\n");
        }

        b.Append("            }\n            finally\n            {\n");
        b.Append("                ").Append(Ns).Append("WorkerCalls.End(__callId);\n");
        b.Append("            }\n        }\n");
    }

    private static string Notify(Contract contract, Value v) => v.Kind == Kind.Json
        ? "static (__id, __channel, __value) => " + Ns + "WorkerCalls.NotifyStr(__id, __channel, " + Ns + "WorkerJson.Serialize<" + v.Type + ">(__value, " + contract.TypeInfo(v.Type) + "))"
        : "static (__id, __channel, __value) => " + Ns + "WorkerCalls.Notify" + v.Suffix + "(__id, __channel, __value)";

    private static string EmitControl() =>
        "// <auto-generated/>\n#nullable enable\n\nnamespace NetWasm.UI.Generated\n{\n" +
        "    internal static partial class WorkerControl\n    {\n" +
        "        [global::System.Runtime.InteropServices.JavaScript.JSExport(\"__nwui_cancel\")]\n" +
        "        public static void Cancel(int callId, int reason) => " + Ns + "WorkerCalls.Cancel(callId);\n\n" +
        "        [global::System.Runtime.InteropServices.JavaScript.JSExport(\"__nwui_credit\")]\n" +
        "        public static void Credit(int callId, int count) => " + Ns + "WorkerCalls.Credit(callId, count);\n" +
        "    }\n}\n";

    // Helpers ----------------------------------------------------------------------------------------------------

    private static bool IsReference(Kind kind) => kind is Kind.Str or Kind.Bytes or Kind.I32Array or Kind.F32Array or Kind.F64Array;

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Sanitize(string name)
    {
        var b = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            b.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? c : '_');
        }

        return b.ToString();
    }

    private sealed class Registration : System.IEquatable<Registration>
    {
        public Registration(string attribute, bool pool, string contract, string? worker, string proxy, string proxySource, DiagnosticDescriptor? descriptor, string? error, Location? location)
        {
            Attribute = attribute;
            Pool = pool;
            Contract = contract;
            Worker = worker;
            Proxy = proxy;
            ProxySource = proxySource;
            Descriptor = descriptor;
            Error = error;
            Location = location;
        }

        public string Attribute { get; }

        public bool Pool { get; }

        public string Contract { get; }

        public string? Worker { get; }

        public string Proxy { get; }

        public string ProxySource { get; }

        public DiagnosticDescriptor? Descriptor { get; }

        public string? Error { get; }

        public Location? Location { get; }

        public static Registration Failed(DiagnosticDescriptor descriptor, Location location, string error) =>
            new(string.Empty, false, string.Empty, null, string.Empty, string.Empty, descriptor, error, location);

        public bool Equals(Registration? other) =>
            other is not null && Attribute == other.Attribute && Pool == other.Pool && Contract == other.Contract && Worker == other.Worker &&
            ProxySource == other.ProxySource && Error == other.Error && Equals(Location, other.Location);

        public override bool Equals(object? obj) => Equals(obj as Registration);

        public override int GetHashCode() => (Attribute + "|" + Contract + "|" + Error).GetHashCode();
    }

    private sealed class ServiceExports : System.IEquatable<ServiceExports>
    {
        public ServiceExports(string hintName, string source, string? error, Location? location)
        {
            HintName = hintName;
            Source = source;
            Error = error;
            Location = location;
        }

        public string HintName { get; }

        public string Source { get; }

        public string? Error { get; }

        public Location? Location { get; }

        public static ServiceExports Failed(Location location, string error) => new(string.Empty, string.Empty, error, location);

        public bool Equals(ServiceExports? other) =>
            other is not null && HintName == other.HintName && Source == other.Source && Error == other.Error && Equals(Location, other.Location);

        public override bool Equals(object? obj) => Equals(obj as ServiceExports);

        public override int GetHashCode() => (HintName + "|" + Error).GetHashCode();
    }
}
