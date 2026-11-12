using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.UI.Generator;
using NetWasm.UI.Reactive;
using NetWasm.UI.Tests.Templates;

namespace NetWasm.UI.Tests;

public enum ReactiveMode
{
    Off,
    On,
}

public partial class ReactiveStore
{
    [Reactive] public partial int Count { get; set; }

    [Reactive("Ada")] public partial string Name { get; private set; }

    [Reactive(ReactiveMode.On)] public partial ReactiveMode Mode { get; set; }

    [Reactive(1.5f)] public partial double Ratio { get; set; }

    [Reactive] public partial string? Note { get; set; }

    [Reactive(3)] public static partial int Shared { get; set; }

    public void Rename(string name) => Name = name;

    public partial class Nested<T>
    {
        [Reactive] public partial T? Item { get; set; }
    }
}

public sealed class ReactiveGeneratorTests : RendererTest
{
    [Fact]
    public void Properties_start_at_their_initial_values()
    {
        var s = new ReactiveStore();

        Assert.Equal(0, s.Count);
        Assert.Equal("Ada", s.Name);
        Assert.Equal(ReactiveMode.On, s.Mode);
        Assert.Equal(1.5, s.Ratio);
        Assert.Null(s.Note);
        Assert.Equal(3, ReactiveStore.Shared);
        Assert.Null(new ReactiveStore.Nested<string>().Item);
    }

    [Fact]
    public void Properties_are_tracked_and_skip_equal_writes()
    {
        var s = new ReactiveStore();
        var runs = 0;
        var seen = string.Empty;
        _ = new Effect(() =>
        {
            runs++;
            seen = s.Name + s.Count.ToString();
        });

        s.Count++;
        s.Count = 1;
        s.Rename("Grace");

        Assert.Equal(3, runs);
        Assert.Equal("Grace1", seen);
    }

    [Fact]
    public void Signal_properties_expose_the_backing_signal()
    {
        var s = new ReactiveStore();

        Signal<int> count = s.CountSignal;
        count.Value = 5;
        Assert.Equal(5, s.Count);

        // A narrower setter exposes the signal read-only.
        Assert.Equal(typeof(IReadOnlySignal<string>), typeof(ReactiveStore).GetProperty(nameof(ReactiveStore.NameSignal))!.PropertyType);
        IReadOnlySignal<string> name = s.NameSignal;
        s.Rename("Grace");
        Assert.Equal("Grace", name.Value);
    }

    [Fact]
    public void Template_properties_render_and_react()
    {
        var c = new Clicker();
        App.Mount(c, "#app");
        Assert.Equal("<p class=\"clicker\"><button>Clicks: 1</button><!----></p>", Dom.Html);

        Fire(Dom.Query("button")!, "click");
        c.Rename("Taps");

        Assert.Equal(2, c.Clicks);
        Assert.Equal("<p class=\"clicker\"><button>Taps: 2</button><strong>busy</strong><!----></p>", Dom.Html);
    }

    [Theory]
    [InlineData("public class S { [Reactive] public partial int X { get; set; } }", "must be declared partial")]
    [InlineData("public partial class S { [Reactive] public int X { get; set; } }", "must be declared partial")]
    [InlineData("public partial class S { [Reactive] public partial int X { get; init; } }", "get and a set accessor")]
    [InlineData("public partial class S { [Reactive] public partial int X { get; } }", "get and a set accessor")]
    [InlineData("public partial struct S { [Reactive] public partial int X { get; set; } }", "class or record class")]
    [InlineData("public partial class S { [Reactive(\"x\")] public partial int X { get; set; } }", "does not convert")]
    [InlineData("public partial class S { [Reactive(new[] { 1 })] public partial int[] X { get; set; } }", "cannot be an array")]
    [InlineData("public partial class S { [Reactive] public partial int X { get; set; } public partial int X { get => 0; set { } } }", "already has an implementation")]
    public void Invalid_properties_report_NWUI020(string source, string message)
    {
        var diagnostics = Run("using NetWasm.UI.Reactive;\n" + source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("NWUI020", d.Id);
        Assert.Contains(message, d.GetMessage());
    }

    [Fact]
    public void Invalid_template_properties_report_NWUI002()
    {
        var t = TemplateCompiler.Compile("C:\\p\\Bad.nwui", "<p></p>\n@code {\n    [Reactive] public int X { get; set; }\n}\n", "App", "C:\\p");

        Assert.Equal("NWUI002", t.Diagnostic!.Id);
        Assert.Contains("must be declared partial", t.Diagnostic.Message);
        Assert.Equal(2, t.Diagnostic.Lines.Start.Line);
        Assert.Equal(26, t.Diagnostic.Lines.Start.Character);
    }

    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var refs = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(Component).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "T",
            new[] { CSharpSyntaxTree.ParseText(source, path: "C:\\p\\S.cs") },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver.Create(new ReactiveGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }
}
