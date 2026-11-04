using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetWasm.UI.Generator;

/// <summary>
/// Rewrites <c>Services.AddLazy&lt;TService, TImplementation&gt;()</c> and <c>AddLazy&lt;T&gt;()</c> with C# interceptors into
/// <c>AddLazy&lt;TService&gt;(() =&gt; new TImplementation(Inject&lt;A&gt;(), …))</c>, so services are wired without reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ServiceGenerator : IIncrementalGenerator
{
    public static readonly DiagnosticDescriptor ConstructorError = new(
        "NWUI010", "Service cannot be constructed", "{0}", "NetWasm.UI.Services", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var calls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    ArgumentList.Arguments.Count: 0,
                    Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "AddLazy" } },
                },
                static (ctx, ct) => Analyze(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(calls.Where(static c => c.Error is not null), static (spc, c) =>
            spc.ReportDiagnostic(Diagnostic.Create(ConstructorError, c.ErrorLocation, c.Error)));

        context.RegisterSourceOutput(
            calls.Where(static c => c.Error is null).Collect(),
            static (spc, all) =>
            {
                if (!all.IsEmpty)
                {
                    spc.AddSource("NetWasm.UI.Services.g.cs", Emit(all));
                }
            });
    }

    private static ServiceCall? Analyze(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method
            || method.Parameters.Length != 0
            || method.ContainingType is not { Name: "Services" } owner
            || owner.ContainingNamespace?.ToDisplayString() != "NetWasm.UI")
        {
            return null;
        }

        var service = method.TypeArguments[0];
        var implementation = method.TypeArguments[method.TypeArguments.Length - 1];
        var location = ((MemberAccessExpressionSyntax)invocation.Expression).Name.GetLocation();
        var compilation = ctx.SemanticModel.Compilation;
        var implName = implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (service is ITypeParameterSymbol || implementation is not INamedTypeSymbol type || type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
        {
            return ServiceCall.Failed(location, $"'{implName}' must be a non-abstract class known at compile time to be created by AddLazy<…>(). Register a factory instead: AddLazy(() => new …).");
        }

        if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) || !compilation.IsSymbolAccessibleWithin(service, compilation.Assembly))
        {
            return ServiceCall.Failed(location, $"'{implName}' and the service type must be public or internal (not private or protected nested types) to be created by AddLazy<…>().");
        }

        IMethodSymbol? ctor = null;
        foreach (var candidate in type.InstanceConstructors)
        {
            if (!compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
            {
                continue;
            }

            if (ctor is not null)
            {
                return ServiceCall.Failed(location, $"'{implName}' has more than one accessible constructor; AddLazy<…>() needs exactly one. Register a factory instead: AddLazy(() => new …).");
            }

            ctor = candidate;
        }

        if (ctor is null)
        {
            return ServiceCall.Failed(location, $"'{implName}' has no public or internal constructor.");
        }

        var parameters = new StringBuilder();
        foreach (var p in ctor.Parameters)
        {
            if (p.RefKind != RefKind.None || p.IsParams)
            {
                return ServiceCall.Failed(location, $"Constructor parameter '{p.Name}' of '{implName}' is ref, out, in or params, which AddLazy<…>() cannot inject.");
            }

            parameters.Append(parameters.Length == 0 ? string.Empty : ", ")
                .Append("global::NetWasm.UI.Html.Inject<")
                .Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">()");
        }

        // The interceptor API is marked experimental in Roslyn 4.12; ASP.NET's and the config binder's generators use it too.
#pragma warning disable RSEXPERIMENTAL002
        var interceptable = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptable is null)
        {
            return null;
        }

        return new ServiceCall(
            interceptable.GetInterceptsLocationAttributeSyntax(),
#pragma warning restore RSEXPERIMENTAL002
            method.TypeArguments.Length == 2,
            service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            parameters.ToString(),
            null,
            null);
    }

    private static string Emit(ImmutableArray<ServiceCall> calls)
    {
        var b = new StringBuilder();
        b.Append("// <auto-generated/>\n#nullable enable\n\n");
        b.Append("namespace NetWasm.UI.Generated\n{\n");
        b.Append("    internal static class ServiceRegistrations\n    {\n");

        // Calls with the same type arguments share one interceptor carrying several attributes.
        var groups = calls
            .GroupBy(static c => (c.Generic, c.Service, c.Implementation, c.Parameters))
            .OrderBy(static g => g.Key.Service + "|" + g.Key.Implementation, System.StringComparer.Ordinal);
        var i = 0;
        foreach (var group in groups)
        {
            foreach (var attribute in group.Select(static c => c.Attribute).OrderBy(static a => a, System.StringComparer.Ordinal))
            {
                b.Append("        ").Append(attribute).Append('\n');
            }

            var (_, service, implementation, parameters) = group.Key;
            b.Append("        public static global::NetWasm.UI.Services AddLazy").Append(i++)
                .Append("(this global::NetWasm.UI.Services services) =>\n")
                .Append("            services.AddLazy<").Append(service).Append(">(static () => new ")
                .Append(implementation).Append('(').Append(parameters).Append("));\n\n");
        }

        b.Append("    }\n}\n\n");
        b.Append("namespace System.Runtime.CompilerServices\n{\n");
        b.Append("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]\n");
        b.Append("    file sealed class InterceptsLocationAttribute : global::System.Attribute\n    {\n");
        b.Append("        public InterceptsLocationAttribute(int version, string data)\n        {\n        }\n    }\n}\n");
        return b.ToString();
    }

    private sealed class ServiceCall : System.IEquatable<ServiceCall>
    {
        public ServiceCall(string attribute, bool generic, string service, string implementation, string parameters, string? error, Location? errorLocation)
        {
            Attribute = attribute;
            Generic = generic;
            Service = service;
            Implementation = implementation;
            Parameters = parameters;
            Error = error;
            ErrorLocation = errorLocation;
        }

        public string Attribute { get; }

        public bool Generic { get; }

        public string Service { get; }

        public string Implementation { get; }

        public string Parameters { get; }

        public string? Error { get; }

        public Location? ErrorLocation { get; }

        public static ServiceCall Failed(Location location, string error) =>
            new(string.Empty, false, string.Empty, string.Empty, string.Empty, error, location);

        public bool Equals(ServiceCall? other) =>
            other is not null && Attribute == other.Attribute && Generic == other.Generic && Service == other.Service &&
            Implementation == other.Implementation && Parameters == other.Parameters && Error == other.Error &&
            Equals(ErrorLocation, other.ErrorLocation);

        public override bool Equals(object? obj) => Equals(obj as ServiceCall);

        public override int GetHashCode() => (Attribute + "|" + Service + "|" + Implementation + "|" + Error).GetHashCode();
    }
}