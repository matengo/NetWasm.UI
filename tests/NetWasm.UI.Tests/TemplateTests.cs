using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.UI.Dom;
using NetWasm.UI.Generator;
using NetWasm.UI.Reactive;
using NetWasm.UI.Routing;
using NetWasm.UI.Tests.Templates;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public sealed record TemplateItem(int Id, string Title, bool Done);

/// <summary>Renders the components generated from Templates/*.nwui.</summary>
public partial class TemplateTests : RendererTest
{
    public static readonly Context<string> Theme = new("Theme", "light");

    public override void Dispose()
    {
        Router.Reset();
        base.Dispose();
    }

    [Fact]
    public void Greeting_renders_text_expressions_attributes_and_entities()
    {
        App.Mount(new Greeting { Name = "Ada" }, "#app");

        Assert.Equal(
            "<div class=\"greeting\">" +
            "<h1>Hello, Ada!</h1>" +
            "<p data-double=\"0\" title=\"Count is 0\">Count: 0 · doubled: 0</p>" +
            "<button id=\"inc\">+1</button>" +
            "<input>" +
            "<input type=\"checkbox\">" +
            "<span class=\"mail\">me@example.com @home <3> AB</span>" +
            "<p class=\"value\">hi</p>" +
            "</div>",
            Dom.Html);
    }

    [Fact]
    public void Greeting_reacts_to_clicks_bind_and_ref()
    {
        var g = new Greeting();
        App.Mount(g, "#app");
        var button = Dom.Query("button")!;

        Fire(button, "click");
        Fire(button, "click");
        Fire(button, "click");

        Assert.Equal(3, g.Count.Value);
        Assert.Equal("<p data-double=\"6\" title=\"Count is 3\">Count: 3 · doubled: 6</p>", Dom.Query("p")!.ToHtml());
        Assert.True(button.Attributes.ContainsKey("disabled"));

        var input = Dom.Query("input")!;
        Assert.Equal(input.Id, g.Input.Id);
        Assert.Equal("hi", input.Properties["value"]);
        Fire(input, "input", "yo");
        Assert.Equal("yo", Dom.Query("p", n => n.Attributes.GetValueOrDefault("class") == "value")!.Children[0].Text);

        var checkbox = Dom.Query("input", n => n.Attributes.GetValueOrDefault("type") == "checkbox")!;
        Assert.Equal(true, checkbox.Properties["checked"]);
    }

    [Fact]
    public void Dashboard_if_else_if_else_chain_switches_branches()
    {
        var d = new Dashboard();
        App.Mount(d, "#app");
        Assert.NotNull(Dom.Query("p", Has("zero")));
        Assert.Null(Dom.Query("em"));

        d.Level.Value = 2;
        Assert.Null(Dom.Query("p", Has("zero")));
        Assert.Equal("<p class=\"few\">few: 2</p>", Dom.Query("p", Has("few"))!.ToHtml());
        Assert.NotNull(Dom.Query("em"));

        d.Level.Value = 1;
        Assert.Equal("<p class=\"few\">few: 1</p>", Dom.Query("p", Has("few"))!.ToHtml());

        d.Level.Value = 5;
        Assert.Null(Dom.Query("p", Has("few")));
        Assert.NotNull(Dom.Query("p", Has("many")));
    }

    [Fact]
    public void Dashboard_foreach_renders_keyed_items_from_signal_and_list()
    {
        var d = new Dashboard();
        d.Items.Value = new[] { new TemplateItem(1, "One", false), new TemplateItem(2, "Two", true) };
        App.Mount(d, "#app");

        Assert.Equal(
            "<ul><li class=\"item open\">One</li><li class=\"item done\">Two</li><!----></ul>",
            Dom.Query("ul")!.ToHtml());
        Assert.Equal("<ol><li>a</li><li>b</li><!----></ol>", Dom.Query("ol")!.ToHtml());

        var two = Dom.Query("li", n => n.Children.Count > 0 && n.Children[0].Text == "Two")!;
        d.Items.Value = new[] { new TemplateItem(2, "Two", true), new TemplateItem(3, "Three", false) };

        // Same key → same DOM node.
        Assert.Same(two, Dom.Query("li", n => n.Children.Count > 0 && n.Children[0].Text == "Two"));
        Assert.Equal(
            "<ul><li class=\"item done\">Two</li><li class=\"item open\">Three</li><!----></ul>",
            Dom.Query("ul")!.ToHtml());

        d.Tags.Value = new[] { "c" };
        Assert.Equal("<ol><li>c</li><!----></ol>", Dom.Query("ol")!.ToHtml());
    }

    [Fact]
    public void Dashboard_components_render_links_and_context()
    {
        Router.Install(new MemoryHistory("/about"));
        App.Mount(new Lambda(() =>
        {
            Provide(Theme, "dark");
            return Div(new Dashboard { Extra = Span(Class("extra"), "!") });
        }), "#app");

        Assert.Equal("<span class=\"badge\">static: 0</span>", Dom.Query("span", Has("badge"))!.ToHtml());
        Assert.NotNull(Dom.Query("span", Has("extra")));
        Assert.Equal("<a class=\"lnk\" href=\"/home\">Home</a>", Dom.Query("a", n => n.Attributes.GetValueOrDefault("href") == "/home")!.ToHtml());
        Assert.Equal("<a class=\"active\" href=\"/about\">About</a>", Dom.Query("a", n => n.Attributes.GetValueOrDefault("href") == "/about")!.ToHtml());
        Assert.Equal("<p class=\"theme\">dark</p>", Dom.Query("p", Has("theme"))!.ToHtml());
    }

    [Fact]
    public void Dashboard_events_use_handlers_and_modifier_flags()
    {
        var d = new Dashboard();
        App.Mount(d, "#app");
        var form = Dom.Query("form")!;
        var link = Dom.Query("a", n => n.Attributes.GetValueOrDefault("href") == "/x")!;

        Assert.Equal((byte)(ListenerFlags.PreventDefault | ListenerFlags.StopPropagation), Assert.Single(form.Listeners).Value.Flags);
        Assert.Equal((byte)ListenerFlags.PreventDefault, Assert.Single(link.Listeners).Value.Flags);

        Fire(form, "submit");
        Fire(link, "click");

        Assert.Equal(1, d.Submitted);
        Assert.Equal("click", d.Clicked);
    }

    // ---- compiler ----

    [Theory]
    [InlineData("<div></div><p></p>", "NWUI002", "exactly one root")]
    [InlineData("<div><span></div>", "NWUI001", "Expected '</span>'")]
    [InlineData("<div>", "NWUI001", "never closed")]
    [InlineData("</div>", "NWUI001", "Unexpected closing tag")]
    [InlineData("<div>@ </div>", "NWUI001", "'@@'")]
    [InlineData("<div>@for (;;) { }</div>", "NWUI003", "not supported")]
    [InlineData("<div @foo=\"x\"></div>", "NWUI003", "Unknown directive")]
    [InlineData("<div><Foo>child</Foo></div>", "NWUI003", "child content")]
    [InlineData("<div>@if (x) { <a></a><b></b> }</div>", "NWUI002", "exactly one root")]
    [InlineData("<div @onclick:preventDefault></div>", "NWUI002", "without an '@onclick' handler")]
    [InlineData("@if (x) { <a></a> }", "NWUI002", "wrap it in an element")]
    [InlineData("<div>@(a</div>", "NWUI001", "Unbalanced")]
    public void Compiler_reports_template_errors(string template, string id, string message)
    {
        var result = TemplateCompiler.Compile("C:\\p\\Bad.nwui", template, "App", "C:\\p\\");

        Assert.Null(result.Source);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(id, result.Diagnostic!.Id);
        Assert.Contains(message, result.Diagnostic.Message);
    }

    [Fact]
    public void Compiler_points_errors_at_the_template_position()
    {
        var result = TemplateCompiler.Compile("C:\\p\\Bad.nwui", "<div>\n  <span>\n</div>", "App", "C:\\p\\");

        var lines = result.Diagnostic!.Lines;
        Assert.Equal(2, lines.Start.Line);
        Assert.Equal(0, lines.Start.Character);
    }

    [Fact]
    public void Namespace_comes_from_root_namespace_and_folder_or_directive()
    {
        var nested = TemplateCompiler.Compile("C:\\p\\Pages\\Admin\\Home.nwui", "<div></div>", "App", "C:\\p\\");
        Assert.Contains("namespace App.Pages.Admin;", nested.Source);
        Assert.Contains("public partial class Home : global::NetWasm.UI.Component", nested.Source);
        Assert.Equal("Pages.Admin.Home.nwui.g.cs", nested.HintName);

        var explicitNs = TemplateCompiler.Compile("C:\\p\\Pages\\Home.nwui", "@namespace My.Ui\n<div></div>", "App", "C:\\p\\");
        Assert.Contains("namespace My.Ui;", explicitNs.Source);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("\n   \n  ", "")]
    [InlineData("  Hello  ", "  Hello  ")]
    [InlineData("\n    Hello\n    world\n  ", "Hello world")]
    [InlineData("Count: ", "Count: ")]
    public void Whitespace_is_collapsed_like_jsx(string raw, string expected) =>
        Assert.Equal(expected, TemplateEmitter.Collapse(raw));

    [Fact]
    public void Csharp_errors_in_templates_map_back_to_the_nwui_file()
    {
        const string template = "<div>\n    <p>@Missing.Value</p>\n</div>\n";
        var compilation = CSharpCompilation.Create(
            "T",
            Array.Empty<SyntaxTree>(),
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            new[] { new TemplateGenerator().AsSourceGenerator() },
            new[] { (AdditionalText)new InMemoryText("C:\\p\\Oops.nwui", template) });

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        Assert.Empty(genDiagnostics);
        var error = Assert.Single(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0103", error.Id);
        var mapped = error.Location.GetMappedLineSpan();
        Assert.Equal("C:\\p\\Oops.nwui", mapped.Path);
        Assert.Equal(1, mapped.StartLinePosition.Line);
        Assert.Equal(8, mapped.StartLinePosition.Character);
    }

    [Fact]
    public void Generator_reports_template_errors_as_diagnostics()
    {
        var compilation = CSharpCompilation.Create("T", Array.Empty<SyntaxTree>(), References());
        var driver = CSharpGeneratorDriver.Create(
            new[] { new TemplateGenerator().AsSourceGenerator() },
            new[] { (AdditionalText)new InMemoryText("C:\\p\\Bad.nwui", "<div>\n<p></div>") });

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var d = Assert.Single(diagnostics);
        Assert.Equal("NWUI001", d.Id);
        Assert.Equal("C:\\p\\Bad.nwui", d.Location.GetLineSpan().Path);
        Assert.Equal(1, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    private static Func<FakeDomHost.FakeNode, bool> Has(string cls) =>
        n => n.Attributes.GetValueOrDefault("class") == cls;

    private static List<MetadataReference> References()
    {
        var refs = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(Component).Assembly.Location));
        return refs;
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default) =>
            Microsoft.CodeAnalysis.Text.SourceText.From(text);
    }

    private sealed class Lambda(Func<Node> build) : Component
    {
        protected override Node Build() => build();
    }
}
