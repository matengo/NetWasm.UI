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

    [Fact]
    public void Card_renders_child_content_and_named_slots()
    {
        App.Mount(new CardHost(), "#app");

        var cards = Dom.QueryAll("div", Has("card"));
        Assert.Equal(4, cards.Count);
        Assert.Equal(
            "<div class=\"card\"><h2>Plain</h2><!----><div class=\"body\"><p class=\"msg\">Hello Ada</p>and more</div></div>",
            cards[0].ToHtml());
        Assert.Equal(
            "<div class=\"card\"><h2>Slots</h2><header><em>head Ada</em></header><!----><div class=\"body\"><span><!----></span></div></div>",
            cards[1].ToHtml());
        Assert.Equal(
            "<div class=\"card\"><h2>Empty</h2><!----><div class=\"body\"></div></div>",
            cards[2].ToHtml());
    }

    [Fact]
    public void Card_child_content_stays_reactive_to_the_parent_state()
    {
        var host = new CardHost();
        App.Mount(host, "#app");

        host.Name.Value = "Grace";
        host.Open.Value = true;

        Assert.Equal("<p class=\"msg\">Hello Grace</p>", Dom.Query("p", Has("msg"))!.ToHtml());
        Assert.Equal("<em>head Grace</em>", Dom.Query("em")!.ToHtml());
        Assert.Equal("<i>Grace</i>", Dom.Query("i")!.ToHtml());
        Assert.NotNull(Dom.Query("b", Has("open")));
    }

    [Fact]
    public void Card_child_content_is_released_with_the_component()
    {
        var host = new CardHost();
        App.Mount(host, "#app");
        var italic = Dom.Query("i")!;
        var italicText = italic.Children[0];

        host.Show.Value = false;

        Assert.Null(Dom.Query("i"));
        Assert.False(Dom.Nodes.ContainsKey(italic.Id));
        Assert.False(Dom.Nodes.ContainsKey(italicText.Id));

        host.Name.Value = "Linus";
        host.Show.Value = true;
        Assert.Equal("<i>Linus</i>", Dom.Query("i")!.ToHtml());
    }

    [Fact]
    public void Slot_can_be_built_from_csharp()
    {
        App.Mount(new Lambda(() => Div(new Card
        {
            Title = "C#",
            Header = new Slot(() => Strong("h")),
            ChildContent = new Slot(() => new Content[] { "a", Span("b") }),
        })), "#app");

        Assert.Equal(
            "<div class=\"card\"><h2>C#</h2><header><strong>h</strong></header><!----><div class=\"body\">a<span>b</span></div></div>",
            Dom.Query("div", Has("card"))!.ToHtml());
    }

    [Fact]
    public void Reactive_props_render_values_and_defaults()
    {
        App.Mount(new MeterHost(), "#app");

        var meters = Dom.QueryAll("div");
        Assert.Equal(
            "<div class=\"meter low\" data-level=\"2\"><b>Level of Ada</b>: 2<!----><i class=\"snap\">2</i></div>",
            meters[0].ToHtml());
        Assert.Equal(
            "<div class=\"meter low\" data-level=\"1\"><b>fixed</b>: 1<!----><i class=\"snap\">1</i></div>",
            meters[1].ToHtml());
        Assert.Equal(new[] { "<span>Ada</span>", "<span>literal</span>" }, Dom.QueryAll("span").Where(s => s.Attributes.Count == 0).Select(s => s.ToHtml()));
        Assert.Equal("<span class=\"badge\">plain: 2</span>", Dom.Query("span", Has("badge"))!.ToHtml());
    }

    [Fact]
    public void Reactive_props_follow_the_parent_signals_but_plain_props_do_not()
    {
        var host = new MeterHost();
        App.Mount(host, "#app");

        host.LevelSignal.Value = 5;
        host.NameSignal.Value = "Grace";

        Assert.Equal(
            "<div class=\"meter high\" data-level=\"5\"><b>Level of Grace</b>: 5<em>hot</em><!----><i class=\"snap\">2</i></div>",
            Dom.QueryAll("div")[0].ToHtml());
        Assert.Equal("<span>Grace</span>", Dom.QueryAll("span").First(s => s.Attributes.Count == 0).ToHtml());
        Assert.Equal("<span class=\"badge\">plain: 2</span>", Dom.Query("span", Has("badge"))!.ToHtml());
    }

    [Fact]
    public void Prop_wraps_values_signals_and_getters()
    {
        var signal = new Signal<int>(1);
        Prop<int> fixedValue = 3;
        Prop<int> fromSignal = signal;
        var fromGetter = new Prop<int>(() => signal.Value * 10);

        Assert.False(fixedValue.IsReactive);
        Assert.True(fromSignal.IsReactive);
        Assert.Equal(3, fixedValue.Value);
        signal.Value = 2;
        Assert.Equal(2, fromSignal.Value);
        Assert.Equal(20, fromGetter.Value);
        Assert.Equal("20", fromGetter.ToString());
        int plain = fromSignal;
        Assert.Equal(2, plain);
        Assert.Equal(0, default(Prop<int>).Value);
    }

    [Fact]
    public void Compiler_finds_prop_declarations_and_prop_properties()
    {
        var props = TemplateCompiler.PropsOf(
            "C:\\p\\Widget.nwui",
            "<b>x</b>\n@prop int A = 1;\n@prop List<string> B\n@code {\n public Prop<int> C { get; set; }\n public NetWasm.UI.Prop<bool> D { get; set; }\n public int E { get; set; }\n}\n");

        Assert.Equal(new[] { "Widget.A", "Widget.B", "Widget.C", "Widget.D" }, props.OrderBy(p => p));
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
    [InlineData("<div><Foo><template>x</template></Foo></div>", "NWUI002", "needs slot=")]
    [InlineData("<div><Foo><template slot=\"A\" class=\"x\"></template></Foo></div>", "NWUI003", "only 'slot' is")]
    [InlineData("<div><Foo><template slot=\"1a\"></template></Foo></div>", "NWUI001", "not a valid slot name")]
    [InlineData("<div><Foo><template slot=\"A\"></template><template slot=\"A\"></template></Foo></div>", "NWUI002", "more than once")]
    [InlineData("<div><Foo><template slot=\"ChildContent\"></template>x</Foo></div>", "NWUI002", "loose child content")]
    [InlineData("<div>@if (x) { <a></a><b></b> }</div>", "NWUI002", "exactly one root")]
    [InlineData("<div @onclick:preventDefault></div>", "NWUI002", "without an '@onclick' handler")]
    [InlineData("@if (x) { <a></a> }", "NWUI002", "wrap it in an element")]
    [InlineData("<div>@(a</div>", "NWUI001", "Unbalanced")]
    [InlineData("@prop Count\n<div></div>", "NWUI001", "@prop Type Name")]
    [InlineData("@prop int Count =\n<div></div>", "NWUI001", "default value")]
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
    public void Unknown_props_and_slots_map_back_to_the_nwui_file()
    {
        const string user = "<div>\n  <Plain Size=\"1\">\n    <i>x</i>\n  </Plain>\n</div>\n";
        var compilation = CSharpCompilation.Create(
            "T",
            Array.Empty<SyntaxTree>(),
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            new[] { new TemplateGenerator().AsSourceGenerator() },
            new[]
            {
                (AdditionalText)new InMemoryText("C:\\p\\Plain.nwui", "<b>x</b>"),
                new InMemoryText("C:\\p\\User.nwui", user),
            });

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        Assert.Empty(genDiagnostics);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => (d.Id, d.Location.GetMappedLineSpan()))
            .OrderBy(e => e.Item2.StartLinePosition.Line)
            .ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("CS0117", e.Id));
        Assert.All(errors, e => Assert.Equal("C:\\p\\User.nwui", e.Item2.Path));
        Assert.Equal((1, 9), (errors[0].Item2.StartLinePosition.Line, errors[0].Item2.StartLinePosition.Character));
        Assert.Equal((2, 4), (errors[1].Item2.StartLinePosition.Line, errors[1].Item2.StartLinePosition.Character));
    }

    [Fact]
    public void Prop_declaration_errors_map_back_to_the_nwui_file()
    {
        const string template = "<b>@Size</b>\n@prop Nope Size = 1;\n@prop int Other = \"x\";\n";
        var compilation = CSharpCompilation.Create(
            "T",
            Array.Empty<SyntaxTree>(),
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            new[] { new TemplateGenerator().AsSourceGenerator() },
            new[] { (AdditionalText)new InMemoryText("C:\\p\\Sized.nwui", template) });

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        Assert.Empty(genDiagnostics);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => (d.Id, Span: d.Location.GetMappedLineSpan()))
            .OrderBy(e => e.Span.StartLinePosition.Line)
            .ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("C:\\p\\Sized.nwui", e.Span.Path));
        Assert.Equal(("CS0246", 1, 6), (errors[0].Id, errors[0].Span.StartLinePosition.Line, errors[0].Span.StartLinePosition.Character));
        Assert.Equal(("CS0029", 2, 18), (errors[1].Id, errors[1].Span.StartLinePosition.Line, errors[1].Span.StartLinePosition.Character));
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
