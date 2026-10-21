using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    public static CompiledTemplate Compile(string path, string text, string? rootNamespace, string? projectDir)
    {
        var className = SafeIdentifier(Path.GetFileNameWithoutExtension(path));
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

            var source = TemplateEmitter.Emit(file, text, path, ns, className);
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

        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var root = Path.GetFullPath(projectDir).TrimEnd('\\', '/');
        if (!dir.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return dir.Substring(root.Length).Trim('\\', '/');
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

        var templates = context.AdditionalTextsProvider
            .Where(static t => t.Path.EndsWith(".nwui", System.StringComparison.OrdinalIgnoreCase))
            .Select(static (t, ct) => (t.Path, Text: t.GetText(ct)?.ToString() ?? string.Empty))
            .Combine(options)
            .Select(static (pair, _) => TemplateCompiler.Compile(pair.Left.Path, pair.Left.Text, pair.Right.Namespace, pair.Right.ProjectDir));

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
