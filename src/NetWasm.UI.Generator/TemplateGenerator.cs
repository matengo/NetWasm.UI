using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetWasm.UI.Generator;

/// <summary>Result of compiling one template (equatable so the incremental pipeline can cache it).</summary>
internal sealed class CompiledTemplate : System.IEquatable<CompiledTemplate>
{
    public CompiledTemplate(string path, string hintName, string? source, TemplateDiagnostic? diagnostic)
    {
        Path = path;
        HintName = hintName;
        Source = source;
        Diagnostic = diagnostic;
    }

    public string Path { get; }
    public string HintName { get; }
    public string? Source { get; }
    public TemplateDiagnostic? Diagnostic { get; }

    public bool Equals(CompiledTemplate? other) =>
        other is not null && Path == other.Path && HintName == other.HintName && Source == other.Source && Equals(Diagnostic, other.Diagnostic);

    public override bool Equals(object? obj) => Equals(obj as CompiledTemplate);

    public override int GetHashCode() => (Path.GetHashCode() * 397) ^ (Source?.GetHashCode() ?? 0);
}

internal sealed class TemplateDiagnostic : System.IEquatable<TemplateDiagnostic>
{
    public TemplateDiagnostic(string id, string message, TextSpan span, LinePositionSpan lines)
    {
        Id = id;
        Message = message;
        Span = span;
        Lines = lines;
    }

    public string Id { get; }
    public string Message { get; }
    public TextSpan Span { get; }
    public LinePositionSpan Lines { get; }

    public bool Equals(TemplateDiagnostic? other) =>
        other is not null && Id == other.Id && Message == other.Message && Span == other.Span;

    public override bool Equals(object? obj) => Equals(obj as TemplateDiagnostic);

    public override int GetHashCode() => Id.GetHashCode() ^ Span.GetHashCode();
}

internal static class TemplateCompiler
{
    public static readonly DiagnosticDescriptor ParseError = new(
        "NWUI001", "Template syntax error", "{0}", "NetWasm.UI.Templates", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StructureError = new(
        "NWUI002", "Template structure error", "{0}", "NetWasm.UI.Templates", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedError = new(
        "NWUI003", "Unsupported template feature", "{0}", "NetWasm.UI.Templates", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static CompiledTemplate Compile(string path, string text, string? rootNamespace, string? projectDir, string? reactiveProps = null, string? injectedConstructors = null)
    {
        var className = SafeIdentifier(FileStem(path));
        var relativeDir = RelativeDirectory(path, projectDir);
        var hint = Sanitize(relativeDir.Length == 0 ? className : relativeDir.Replace('\\', '.').Replace('/', '.') + "." + className) + ".nwui.g.cs";
        try
        {
            var file = TemplateParser.Parse(text);
            string? ns;
            if (file.Namespace is not null)
            {
                ns = file.Namespace.Code;
            }
            else
            {
                ns = rootNamespace;
                foreach (var part in relativeDir.Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    ns = string.IsNullOrEmpty(ns) ? SafeIdentifier(part) : ns + "." + SafeIdentifier(part);
                }
            }

            var reactive = new HashSet<string>(
                (reactiveProps ?? string.Empty).Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries),
                System.StringComparer.Ordinal);
            foreach (var p in PropsOf(file, className))
            {
                reactive.Add(p);
            }

            var source = TemplateEmitter.Emit(file, text, path, ns, className, reactive, ParseConstructors(injectedConstructors));
            return new CompiledTemplate(path, hint, source, null);
        }
        catch (TemplateException ex)
        {
            var source = SourceText.From(text);
            var start = System.Math.Max(0, System.Math.Min(ex.Start, text.Length));
            var length = System.Math.Max(0, System.Math.Min(ex.Length, text.Length - start));
            var span = new TextSpan(start, length);
            return new CompiledTemplate(path, hint, null, new TemplateDiagnostic(ex.Id, ex.Message, span, source.Lines.GetLinePositionSpan(span)));
        }
    }

    /// <summary>
    /// For a component class whose only accessible constructor takes parameters: its simple name, then the
    /// fully qualified parameter types, separated by tabs. The parent fills the parameters with <c>Inject&lt;T&gt;()</c>.
    /// Null when the class has a parameterless constructor, several candidates, or is not a component.
    /// </summary>
    public static string? InjectedConstructorOf(INamedTypeSymbol? type)
    {
        if (type is null || type.IsAbstract || type.IsStatic || type.TypeParameters.Length > 0 || !MayBeComponent(type))
        {
            return null;
        }

        IMethodSymbol? only = null;
        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                continue;
            }

            if (ctor.Parameters.Length == 0 || only is not null)
            {
                return null;
            }

            only = ctor;
        }

        if (only is null)
        {
            return null;
        }

        var b = new StringBuilder(type.Name);
        foreach (var p in only.Parameters)
        {
            if (p.RefKind != RefKind.None || p.IsParams)
            {
                return null;
            }

            b.Append('\t').Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return b.ToString();
    }

    /// <summary>
    /// A <c>Component</c> subclass, or a partial class whose base may come from a generated <c>.nwui</c> part
    /// (generated sources are not visible to the semantic model in the same run).
    /// </summary>
    private static bool MayBeComponent(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.Name == "Component" && b.ContainingNamespace?.ToDisplayString() == "NetWasm.UI")
            {
                return true;
            }
        }

        if (type.BaseType is { SpecialType: not SpecialType.System_Object })
        {
            return false;
        }

        foreach (var r in type.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is ClassDeclarationSyntax cls && cls.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string[]>? ParseConstructors(string? all)
    {
        if (string.IsNullOrEmpty(all))
        {
            return null;
        }

        var result = new Dictionary<string, string[]>(System.StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var entry in all!.Split('\n'))
        {
            var parts = entry.Split('\t');
            var types = new string[parts.Length - 1];
            System.Array.Copy(parts, 1, types, 0, types.Length);
            if (result.TryGetValue(parts[0], out var existing) && string.Join("\t", existing) != string.Join("\t", types))
            {
                ambiguous.Add(parts[0]);
            }

            result[parts[0]] = types;
        }

        // Two components with the same simple name and different constructors: leave both to the C# compiler.
        foreach (var name in ambiguous)
        {
            result.Remove(name);
        }

        return result;
    }

    /// <summary><c>Class.Prop</c> for every <c>@prop</c> and every <c>Prop&lt;T&gt;</c> property in <c>@code</c> of a template.</summary>
    public static IEnumerable<string> PropsOf(string path, string text)
    {
        TemplateFile file;
        try
        {
            file = TemplateParser.Parse(text);
        }
        catch (TemplateException)
        {
            return System.Array.Empty<string>();
        }

        return PropsOf(file, SafeIdentifier(FileStem(path)));
    }

    private static List<string> PropsOf(TemplateFile file, string className)
    {
        var result = new List<string>();
        foreach (var p in file.Props)
        {
            result.Add(className + "." + p.Name.Code);
        }

        foreach (var block in file.CodeBlocks)
        {
            var tree = CSharpSyntaxTree.ParseText("class __C {" + block.Code + "\n}");
            foreach (var member in tree.GetRoot().DescendantNodes())
            {
                if (member is PropertyDeclarationSyntax { Parent: ClassDeclarationSyntax { Identifier.ValueText: "__C" } } prop && IsPropType(prop.Type))
                {
                    result.Add(className + "." + prop.Identifier.ValueText);
                }
            }
        }

        return result;
    }

    /// <summary><c>Prop&lt;T&gt;</c>, optionally qualified (<c>NetWasm.UI.Prop&lt;T&gt;</c>, <c>global::…</c>).</summary>
    public static bool IsPropType(TypeSyntax type)
    {
        var name = type switch
        {
            QualifiedNameSyntax q => q.Right,
            AliasQualifiedNameSyntax a => a.Name,
            _ => type,
        };
        return name is GenericNameSyntax { Identifier.ValueText: "Prop", TypeArgumentList.Arguments.Count: 1 };
    }

    public static Diagnostic ToDiagnostic(CompiledTemplate t)
    {
        var d = t.Diagnostic!;
        var descriptor = d.Id switch
        {
            "NWUI002" => StructureError,
            "NWUI003" => UnsupportedError,
            _ => ParseError,
        };
        return Diagnostic.Create(descriptor, Location.Create(t.Path, d.Span, d.Lines), d.Message);
    }

    private static string RelativeDirectory(string path, string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir))
        {
            return string.Empty;
        }

        // Paths are compared as strings with either separator so that the result does not depend
        // on the OS the generator runs on.
        var file = path.Replace('\\', '/');
        var root = projectDir!.Replace('\\', '/').TrimEnd('/');
        var cut = file.LastIndexOf('/');
        var dir = cut < 0 ? string.Empty : file.Substring(0, cut);
        if (dir.StartsWith(root, System.StringComparison.OrdinalIgnoreCase) && (dir.Length == root.Length || dir[root.Length] == '/'))
        {
            return dir.Substring(root.Length).Trim('/');
        }

        if (Path.IsPathRooted(path) && Path.IsPathRooted(projectDir))
        {
            return string.Empty;
        }

        var fullDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var fullRoot = Path.GetFullPath(projectDir).TrimEnd('\\', '/');
        if (!fullDir.StartsWith(fullRoot, System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return fullDir.Substring(fullRoot.Length).Trim('\\', '/');
    }

    private static string FileStem(string path)
    {
        var start = System.Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;
        var name = path.Substring(start);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    private static string SafeIdentifier(string name)
    {
        var b = new StringBuilder();
        foreach (var c in name)
        {
            b.Append(CSharpScanner.IsIdentPart(c) ? c : '_');
        }

        if (b.Length == 0 || !CSharpScanner.IsIdentStart(b[0]))
        {
            b.Insert(0, '_');
        }

        var s = b.ToString();
        return SyntaxFacts.GetKeywordKind(s) != SyntaxKind.None ? "@" + s : s;
    }

    private static string Sanitize(string hint)
    {
        var b = new StringBuilder();
        foreach (var c in hint)
        {
            b.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        return b.ToString();
    }
}

/// <summary>Compiles every <c>*.nwui</c> AdditionalFile into a <c>Component</c> partial class.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
        {
            p.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
            p.GlobalOptions.TryGetValue("build_property.ProjectDir", out var dir);
            return (Namespace: ns, ProjectDir: dir);
        });

        var sources = context.AdditionalTextsProvider
            .Where(static t => t.Path.EndsWith(".nwui", System.StringComparison.OrdinalIgnoreCase))
            .Select(static (t, ct) => (t.Path, Text: t.GetText(ct)?.ToString() ?? string.Empty));

        // Which component props are Prop<T> (from .nwui files and hand-written C# components):
        // parents pass those as getters. Kept as one sorted string so the pipeline can compare it cheaply.
        var templateProps = sources.SelectMany(static (t, _) => TemplateCompiler.PropsOf(t.Path, t.Text)).Collect();
        var csharpProps = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is PropertyDeclarationSyntax { Parent: ClassDeclarationSyntax } p && TemplateCompiler.IsPropType(p.Type),
                static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node.Parent!).Identifier.ValueText + "." + ((PropertyDeclarationSyntax)ctx.Node).Identifier.ValueText)
            .Collect();
        var reactiveProps = templateProps.Combine(csharpProps).Select(static (p, _) =>
        {
            var all = new SortedSet<string>(p.Left, System.StringComparer.Ordinal);
            all.UnionWith(p.Right);
            return string.Join("|", all);
        });

        // Components without a parameterless constructor: a component tag fills the parameters with Inject<T>().
        var constructors = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c && (c.ParameterList is not null || c.Members.Any(m => m is ConstructorDeclarationSyntax)),
                static (ctx, ct) => TemplateCompiler.InjectedConstructorOf(ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, ct)))
            .Where(static c => c is not null)
            .Collect()
            .Select(static (all, _) => string.Join("\n", new SortedSet<string>(all!, System.StringComparer.Ordinal)));

        var templates = sources
            .Combine(options)
            .Combine(reactiveProps)
            .Combine(constructors)
            .Select(static (pair, _) => TemplateCompiler.Compile(
                pair.Left.Left.Left.Path, pair.Left.Left.Left.Text, pair.Left.Left.Right.Namespace, pair.Left.Left.Right.ProjectDir, pair.Left.Right, pair.Right));

        context.RegisterSourceOutput(templates, static (spc, t) =>
        {
            if (t.Diagnostic is not null)
            {
                spc.ReportDiagnostic(TemplateCompiler.ToDiagnostic(t));
                return;
            }

            spc.AddSource(t.HintName, SourceText.From(t.Source!, Encoding.UTF8));
        });
    }
}
