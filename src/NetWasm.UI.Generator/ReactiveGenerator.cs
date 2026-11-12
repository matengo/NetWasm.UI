using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetWasm.UI.Generator;

/// <summary>
/// Implements <c>[Reactive] partial</c> properties in C# files with a <c>Signal&lt;T&gt;</c> field and adds a
/// <c>NameSignal</c> property. <c>.nwui</c> templates are handled by <see cref="TemplateEmitter"/>, because a
/// generator does not see another generator's output.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ReactiveGenerator : IIncrementalGenerator
{
    public static readonly DiagnosticDescriptor ReactiveError = new(
        "NWUI020", "Invalid [Reactive] property", "{0}", "NetWasm.UI.Reactive", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var properties = context.SyntaxProvider.ForAttributeWithMetadataName(
            "NetWasm.UI.Reactive.ReactiveAttribute",
            static (node, _) => node is PropertyDeclarationSyntax,
            static (ctx, ct) => Analyze(ctx, ct));

        context.RegisterSourceOutput(properties, static (spc, r) =>
        {
            if (r.Error is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(ReactiveError, r.ErrorLocation, r.Error));
                return;
            }

            spc.AddSource(r.HintName, SourceText.From(r.Source, Encoding.UTF8));
        });
    }

    private static Result Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var syntax = (PropertyDeclarationSyntax)ctx.TargetNode;
        var property = (IPropertySymbol)ctx.TargetSymbol;
        var location = syntax.Identifier.GetLocation();
        var name = property.Name;

        var shape = ReactiveMember.CheckShape(syntax);
        if (shape is not null)
        {
            return Result.Failed(location, $"[Reactive] property '{name}' {shape}");
        }

        if (property.PartialImplementationPart is not null)
        {
            return Result.Failed(location, $"[Reactive] property '{name}' already has an implementation; the generator writes it. Remove the implementing declaration.");
        }

        var declarations = new StringBuilder();
        var depth = 0;
        for (var t = property.ContainingType; t is not null; t = t.ContainingType)
        {
            if (t.TypeKind != TypeKind.Class)
            {
                return Result.Failed(location, $"[Reactive] property '{name}' must be in a class or record class, not a {t.TypeKind.ToString().ToLowerInvariant()}.");
            }

            var partial = false;
            foreach (var r in t.DeclaringSyntaxReferences)
            {
                if (r.GetSyntax(ct) is TypeDeclarationSyntax decl && decl.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    partial = true;
                }
            }

            if (!partial)
            {
                return Result.Failed(location, $"[Reactive] property '{name}' is in '{t.Name}', which must be declared partial.");
            }

            var header = new StringBuilder("partial ").Append(t.IsRecord ? "record " : "class ").Append(t.Name);
            if (t.TypeParameters.Length > 0)
            {
                header.Append('<');
                for (var i = 0; i < t.TypeParameters.Length; i++)
                {
                    header.Append(i == 0 ? string.Empty : ", ").Append(t.TypeParameters[i].Name);
                }

                header.Append('>');
            }

            declarations.Insert(0, header.Append('\n').Append("{\n").ToString());
            depth++;
        }

        var initial = "default!";
        var attribute = ctx.Attributes[0];
        if (attribute.ConstructorArguments.Length == 1)
        {
            var arg = attribute.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                return Result.Failed(location, $"The initial value of [Reactive] property '{name}' cannot be an array. Set it in the constructor, or use a Signal<T> field.");
            }

            if (!arg.IsNull)
            {
                var conversion = ctx.SemanticModel.Compilation.ClassifyConversion(arg.Type!, property.Type);
                if (!conversion.IsImplicit)
                {
                    return Result.Failed(location, $"The initial value of [Reactive] property '{name}' is a {arg.Type!.ToDisplayString()}, which does not convert to {property.Type.ToDisplayString()}.");
                }

                initial = Constant(arg);
            }
        }

        var b = new StringBuilder();
        b.Append("// <auto-generated/>\n#nullable enable\n#pragma warning disable CS1591\n\n");
        var ns = property.ContainingNamespace;
        if (ns is { IsGlobalNamespace: false })
        {
            b.Append("namespace ").Append(ns.ToDisplayString()).Append("\n{\n");
        }

        b.Append(declarations);
        b.Append(ReactiveMember.Emit(syntax, property.Type.ToDisplayString(TypeFormat), initial));
        for (var i = 0; i < depth; i++)
        {
            b.Append("}\n");
        }

        if (ns is { IsGlobalNamespace: false })
        {
            b.Append("}\n");
        }

        var typeName = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
        var hint = new StringBuilder();
        foreach (var c in typeName + "." + name)
        {
            hint.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_');
        }

        return new Result(hint.Append(".Reactive.g.cs").ToString(), b.ToString(), null, null);
    }

    /// <summary>A C# expression for an attribute constant, valid in any file (enums fully qualified, casts for the exact type).</summary>
    private static string Constant(TypedConstant arg)
    {
        var type = arg.Type!;
        if (type.TypeKind == TypeKind.Enum)
        {
            return "(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" +
                SymbolDisplay.FormatPrimitive(arg.Value!, quoteStrings: false, useHexadecimalNumbers: false) + ")";
        }

        switch (arg.Value)
        {
            case double d when double.IsNaN(d) || double.IsInfinity(d):
                return double.IsNaN(d) ? "double.NaN" : d > 0 ? "double.PositiveInfinity" : "double.NegativeInfinity";
            case float f when float.IsNaN(f) || float.IsInfinity(f):
                return float.IsNaN(f) ? "float.NaN" : f > 0 ? "float.PositiveInfinity" : "float.NegativeInfinity";
        }

        var literal = SymbolDisplay.FormatPrimitive(arg.Value!, quoteStrings: true, useHexadecimalNumbers: false);
        return type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_Int32
            ? literal
            : "(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + literal + ")";
    }

    private sealed class Result : System.IEquatable<Result>
    {
        public Result(string hintName, string source, string? error, Location? errorLocation)
        {
            HintName = hintName;
            Source = source;
            Error = error;
            ErrorLocation = errorLocation;
        }

        public string HintName { get; }

        public string Source { get; }

        public string? Error { get; }

        public Location? ErrorLocation { get; }

        public static Result Failed(Location location, string error) => new(string.Empty, string.Empty, error, location);

        public bool Equals(Result? other) =>
            other is not null && HintName == other.HintName && Source == other.Source && Error == other.Error && Equals(ErrorLocation, other.ErrorLocation);

        public override bool Equals(object? obj) => Equals(obj as Result);

        public override int GetHashCode() => (HintName + "|" + Source + "|" + Error).GetHashCode();
    }
}

/// <summary>The members for one <c>[Reactive]</c> property; shared by C# files and <c>.nwui</c> templates.</summary>
internal static class ReactiveMember
{
    /// <summary><c>[Reactive]</c>, <c>[ReactiveAttribute]</c>, optionally qualified.</summary>
    public static AttributeSyntax? Find(PropertyDeclarationSyntax property)
    {
        foreach (var list in property.AttributeLists)
        {
            foreach (var a in list.Attributes)
            {
                var name = a.Name switch
                {
                    QualifiedNameSyntax q => q.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax q => q.Name.Identifier.ValueText,
                    SimpleNameSyntax s => s.Identifier.ValueText,
                    _ => string.Empty,
                };
                if (name is "Reactive" or "ReactiveAttribute")
                {
                    return a;
                }
            }
        }

        return null;
    }

    /// <summary>Null if the property can be implemented, otherwise the end of an error message.</summary>
    public static string? CheckShape(PropertyDeclarationSyntax p)
    {
        if (!p.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return "must be declared partial: [Reactive] public partial int Count { get; set; }";
        }

        if (p.ExpressionBody is not null || p.Initializer is not null || p.AccessorList is null)
        {
            return "must be declared as { get; set; } without a body or initializer. Give the initial value as [Reactive(value)].";
        }

        if (p.Modifiers.Any(SyntaxKind.AbstractKeyword) || p.Modifiers.Any(SyntaxKind.ExternKeyword))
        {
            return "cannot be abstract or extern.";
        }

        var get = false;
        var set = false;
        foreach (var a in p.AccessorList.Accessors)
        {
            if (a.Body is not null || a.ExpressionBody is not null)
            {
                return "must be declared as { get; set; } without a body; the generator implements it.";
            }

            get |= a.IsKind(SyntaxKind.GetAccessorDeclaration);
            set |= a.IsKind(SyntaxKind.SetAccessorDeclaration);
        }

        return get && set && p.AccessorList.Accessors.Count == 2 ? null : "must have a get and a set accessor (init is not supported).";
    }

    /// <summary>The signal field, the property implementation and the <c>NameSignal</c> property, indented for a class body.</summary>
    public static string Emit(PropertyDeclarationSyntax p, string type, string initial)
    {
        var name = p.Identifier.ValueText;
        var field = "__nwReactive_" + name;
        var isStatic = p.Modifiers.Any(SyntaxKind.StaticKeyword);
        var access = new StringBuilder();
        foreach (var m in p.Modifiers)
        {
            if (m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.PrivateKeyword))
            {
                access.Append(m.ValueText).Append(' ');
            }
        }

        if (access.Length == 0)
        {
            access.Append("private ");
        }

        var getModifiers = string.Empty;
        var setModifiers = string.Empty;
        foreach (var a in p.AccessorList!.Accessors)
        {
            var mods = a.Modifiers.Count == 0 ? string.Empty : a.Modifiers.ToString() + " ";
            if (a.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                getModifiers = mods;
            }
            else
            {
                setModifiers = mods;
            }
        }

        var modifiers = new StringBuilder();
        foreach (var m in p.Modifiers)
        {
            modifiers.Append(m.ValueText).Append(' ');
        }

        var s = isStatic ? "static " : string.Empty;
        var signal = "global::NetWasm.UI.Reactive.Signal<" + type + ">";
        var exposed = setModifiers.Length == 0 && getModifiers.Length == 0 ? signal : "global::NetWasm.UI.Reactive.IReadOnlySignal<" + type + ">";
        var b = new StringBuilder();
        b.Append("    private ").Append(s).Append("readonly ").Append(signal).Append(' ').Append(field).Append(" = new(").Append(initial).Append(");\n\n");
        b.Append("    ").Append(modifiers).Append(type).Append(' ').Append(name).Append("\n    {\n");
        b.Append("        ").Append(getModifiers).Append("get => ").Append(field).Append(".Value;\n");
        b.Append("        ").Append(setModifiers).Append("set => ").Append(field).Append(".Value = value;\n    }\n\n");
        b.Append("    /// <summary>The signal behind <see cref=\"").Append(name).Append("\"/>.</summary>\n");
        b.Append("    ").Append(access).Append(s).Append(exposed).Append(' ').Append(name).Append("Signal => ").Append(field).Append(";\n");
        return b.ToString();
    }
}
