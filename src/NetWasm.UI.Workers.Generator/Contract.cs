using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetWasm.UI.Workers.Generator;

/// <summary>How a value crosses between the app and a worker: natively, or as JSON in a string.</summary>
internal enum Kind
{
    Void,
    Bool,
    I32,
    F32,
    F64,
    Str,
    Bytes,
    I32Array,
    F32Array,
    F64Array,
    Json,
}

internal enum Shape
{
    Task,
    Stream,
}

internal enum Role
{
    Value,
    Progress,
    Token,
}

internal sealed class Value
{
    public Value(Kind kind, string type)
    {
        Kind = kind;
        Type = type;
    }

    public Kind Kind { get; }

    /// <summary>The fully qualified C# type.</summary>
    public string Type { get; }

    /// <summary>The suffix of the WorkerCall/WorkerCalls method for this kind (JSON travels as a string).</summary>
    public string Suffix => Kind == Kind.Json ? "Str" : Kind.ToString();
}

internal sealed class Param
{
    public Param(string name, string type, Role role, Value? value)
    {
        Name = name;
        Type = type;
        Role = role;
        Value = value;
    }

    public string Name { get; }

    public string Type { get; }

    public Role Role { get; }

    /// <summary>The value of a <see cref="Role.Value"/> parameter, or the item of an <c>IProgress&lt;T&gt;</c>.</summary>
    public Value? Value { get; }
}

internal sealed class Method
{
    public Method(string name, string export, string returnType, Shape shape, Value result, List<Param> parameters)
    {
        Name = name;
        Export = export;
        ReturnType = returnType;
        Shape = shape;
        Result = result;
        Params = parameters;
    }

    public string Name { get; }

    public string Export { get; }

    public string ReturnType { get; }

    public Shape Shape { get; }

    /// <summary>The awaited result, or the item of a stream.</summary>
    public Value Result { get; }

    public List<Param> Params { get; }

    public string Token => Params.FirstOrDefault(static p => p.Role == Role.Token)?.Name ?? "default";
}

/// <summary>A <c>[WorkerContract]</c> interface, read the same way on the app and the worker side.</summary>
internal sealed class Contract
{
    public const string ContractAttribute = "NetWasm.UI.Workers.WorkerContractAttribute";

    public static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private Contract(string type, string? worker, string? json, List<Method> methods, string? error)
    {
        Type = type;
        Worker = worker;
        Json = json;
        Methods = methods;
        Error = error;
    }

    public string Type { get; }

    public string? Worker { get; }

    public string? Json { get; }

    public List<Method> Methods { get; }

    public string? Error { get; }

    public static bool IsContract(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == ContractAttribute);

    public static Contract Read(INamedTypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var attribute = type.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == ContractAttribute);
        if (attribute is null || type.TypeKind != TypeKind.Interface)
        {
            return Fail(fq, $"'{display}' is not an interface marked [WorkerContract].");
        }

        if (type.IsGenericType)
        {
            return Fail(fq, $"Worker contract '{display}' cannot be generic.");
        }

        string? worker = null;
        string? json = null;
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "Worker" && named.Value.Value is string w && w.Length > 0)
            {
                worker = w;
            }
            else if (named.Key == "JsonContext" && named.Value.Value is ITypeSymbol ctx)
            {
                json = ctx.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        var prefix = Sanitize(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        var methods = new List<Method>();
        var names = new HashSet<string>();
        foreach (var iface in new[] { type }.Concat(type.AllInterfaces))
        {
            var ifaceName = iface.ToDisplayString();
            if (ifaceName is "System.IDisposable" or "System.IAsyncDisposable")
            {
                continue;
            }

            foreach (var member in iface.GetMembers())
            {
                if (member.IsStatic || member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
                {
                    continue;
                }

                if (member is not IMethodSymbol method)
                {
                    return Fail(fq, $"Worker contract '{display}' can only have methods; '{member.Name}' is not one.");
                }

                if (!names.Add(method.Name))
                {
                    return Fail(fq, $"Worker contract '{display}' overloads '{method.Name}'; give each method its own name.");
                }

                var error = ReadMethod(method, prefix, json, out var m);
                if (error is not null)
                {
                    return Fail(fq, $"'{display}.{method.Name}': {error}");
                }

                methods.Add(m!);
            }
        }

        return new Contract(fq, worker, json, methods, null);
    }

    private static Contract Fail(string type, string error) => new(type, null, null, new List<Method>(), error);

    private static string? ReadMethod(IMethodSymbol method, string prefix, string? json, out Method? result)
    {
        result = null;
        if (method.IsGenericMethod)
        {
            return "worker methods cannot be generic.";
        }

        Shape shape;
        ITypeSymbol? resultType = null;
        var returns = method.ReturnType as INamedTypeSymbol;
        var returnName = returns is null ? null : MetadataName(returns);
        switch (returnName)
        {
            case "System.Threading.Tasks.Task":
            case "System.Threading.Tasks.ValueTask":
                shape = Shape.Task;
                break;
            case "System.Threading.Tasks.Task`1":
            case "System.Threading.Tasks.ValueTask`1":
                shape = Shape.Task;
                resultType = returns!.TypeArguments[0];
                break;
            case "System.Collections.Generic.IAsyncEnumerable`1":
                shape = Shape.Stream;
                resultType = returns!.TypeArguments[0];
                break;
            default:
                return "worker methods must return Task, Task<T>, ValueTask, ValueTask<T> or IAsyncEnumerable<T>.";
        }

        var resultValue = resultType is null ? new Value(Kind.Void, "void") : Classify(resultType);
        if (resultValue.Kind == Kind.Json && json is null)
        {
            return NeedsJson(resultType!);
        }

        var parameters = new List<Param>();
        foreach (var p in method.Parameters)
        {
            if (p.RefKind != RefKind.None || p.IsParams)
            {
                return $"parameter '{p.Name}' is ref, out, in or params.";
            }

            var name = SyntaxFacts.GetKeywordKind(p.Name) != SyntaxKind.None ? "@" + p.Name : p.Name;
            var type = p.Type.ToDisplayString(Fq);
            if (p.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                if (parameters.Any(static x => x.Role == Role.Token))
                {
                    return "only one CancellationToken parameter is allowed.";
                }

                parameters.Add(new Param(name, type, Role.Token, null));
            }
            else if (p.Type is INamedTypeSymbol { IsGenericType: true } g && MetadataName(g) == "System.IProgress`1")
            {
                if (parameters.Any(static x => x.Role == Role.Progress))
                {
                    return "only one IProgress<T> parameter is allowed.";
                }

                var item = Classify(g.TypeArguments[0]);
                if (item.Kind == Kind.Json && json is null)
                {
                    return NeedsJson(g.TypeArguments[0]);
                }

                parameters.Add(new Param(name, type, Role.Progress, item));
            }
            else
            {
                var value = Classify(p.Type);
                if (value.Kind == Kind.Json && json is null)
                {
                    return NeedsJson(p.Type);
                }

                parameters.Add(new Param(name, type, Role.Value, value));
            }
        }

        result = new Method(method.Name, prefix + "_" + method.Name, method.ReturnType.ToDisplayString(Fq), shape, resultValue, parameters);
        return null;
    }

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + type.MetadataName : type.MetadataName;

    private static string NeedsJson(ITypeSymbol type) =>
        $"'{type.ToDisplayString()}' does not cross natively; set [WorkerContract(JsonContext = typeof(...))] to a JsonSerializerContext that lists it.";

    public static Value Classify(ITypeSymbol type)
    {
        var fq = type.ToDisplayString(Fq);
        var kind = type.SpecialType switch
        {
            SpecialType.System_Boolean => Kind.Bool,
            SpecialType.System_Int32 => Kind.I32,
            SpecialType.System_Single => Kind.F32,
            SpecialType.System_Double => Kind.F64,
            SpecialType.System_String => Kind.Str,
            _ => type is IArrayTypeSymbol { Rank: 1 } array
                ? array.ElementType.SpecialType switch
                {
                    SpecialType.System_Byte => Kind.Bytes,
                    SpecialType.System_Int32 => Kind.I32Array,
                    SpecialType.System_Single => Kind.F32Array,
                    SpecialType.System_Double => Kind.F64Array,
                    _ => Kind.Json,
                }
                : Kind.Json,
        };
        return new Value(kind, fq);
    }

    private static string Sanitize(string name)
    {
        var b = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            b.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? c : '_');
        }

        return b.ToString();
    }

    public string TypeInfo(string type) => $"global::NetWasm.UI.Workers.WorkerJson.Info<{type}>({Json}.Default)";
}
