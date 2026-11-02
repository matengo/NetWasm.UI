using System;
using System.Collections.Generic;
using System.Globalization;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Terse builder functions. Import with <c>using static NetWasm.UI.Html;</c> so component code reads
/// like markup: <c>Div(Class("card"), H1("Title"), Button("Go", OnClick(_ => ...)))</c>.
/// </summary>
public static class Html
{
    // ---- elements ----

    public static Element El(string tag, params Content[] content) => new(tag, content);

    public static Element Div(params Content[] content) => new("div", content);
    public static Element Span(params Content[] content) => new("span", content);
    public static Element P(params Content[] content) => new("p", content);
    public static Element H1(params Content[] content) => new("h1", content);
    public static Element H2(params Content[] content) => new("h2", content);
    public static Element H3(params Content[] content) => new("h3", content);
    public static Element Ul(params Content[] content) => new("ul", content);
    public static Element Ol(params Content[] content) => new("ol", content);
    public static Element Li(params Content[] content) => new("li", content);
    public static Element Button(params Content[] content) => new("button", content);
    public static Element Input(params Content[] content) => new("input", content);
    public static Element Label(params Content[] content) => new("label", content);
    public static Element Form(params Content[] content) => new("form", content);
    public static Element Section(params Content[] content) => new("section", content);
    public static Element Header(params Content[] content) => new("header", content);
    public static Element Footer(params Content[] content) => new("footer", content);
    public static Element Main(params Content[] content) => new("main", content);
    public static Element Nav(params Content[] content) => new("nav", content);
    public static Element A(params Content[] content) => new("a", content);
    public static Element Strong(params Content[] content) => new("strong", content);
    public static Element Em(params Content[] content) => new("em", content);
    public static Element Small(params Content[] content) => new("small", content);
    public static Element Table(params Content[] content) => new("table", content);
    public static Element Thead(params Content[] content) => new("thead", content);
    public static Element Tbody(params Content[] content) => new("tbody", content);
    public static Element Tr(params Content[] content) => new("tr", content);
    public static Element Th(params Content[] content) => new("th", content);
    public static Element Td(params Content[] content) => new("td", content);
    public static Element Select(params Content[] content) => new("select", content);
    public static Element Option(params Content[] content) => new("option", content);
    public static Element Textarea(params Content[] content) => new("textarea", content);
    public static Element Pre(params Content[] content) => new("pre", content);
    public static Element Code(params Content[] content) => new("code", content);
    public static Element Img(params Content[] content) => new("img", content);
    public static Element Br() => new("br");
    public static Element Hr() => new("hr");

    // ---- text ----

    public static TextNode Text(string text) => new(text);

    /// <summary>Reactive text: re-evaluated whenever a signal read inside <paramref name="text"/> changes.</summary>
    public static TextNode Text(Func<string> text) => new(text);

    /// <summary>Reactive text from any value, formatted with the invariant culture.</summary>
    public static TextNode Text<T>(Func<T> value) => new(() => Format(value()));

    public static TextNode Text<T>(IReadOnlySignal<T> signal) => new(() => Format(signal.Value));

    private static string Format<T>(T value) => TemplateRuntime.Str(value) ?? string.Empty;

    // ---- attributes ----

    public static Attr Attr(string name, string value) => UI.Attr.Static(name, value);
    public static Attr Attr(string name, Func<string?> value) => UI.Attr.Dynamic(name, value);
    public static Attr Class(string value) => UI.Attr.Static("class", value);
    public static Attr Class(Func<string?> value) => UI.Attr.Dynamic("class", value);
    public static Attr Id(string value) => UI.Attr.Static("id", value);
    public static Attr Type(string value) => UI.Attr.Static("type", value);
    public static Attr Href(string value) => UI.Attr.Static("href", value);
    public static Attr Src(string value) => UI.Attr.Static("src", value);
    public static Attr Alt(string value) => UI.Attr.Static("alt", value);
    public static Attr Placeholder(string value) => UI.Attr.Static("placeholder", value);
    public static Attr Title(string value) => UI.Attr.Static("title", value);
    public static Attr Style(string value) => UI.Attr.Static("style", value);
    public static Attr Style(Func<string?> value) => UI.Attr.Dynamic("style", value);
    public static Attr Disabled(Func<bool> value) => UI.Attr.Toggle("disabled", value);
    public static Attr Disabled(bool value = true) => UI.Attr.Toggle("disabled", () => value);
    public static Attr Autofocus() => UI.Attr.Static("autofocus", string.Empty);

    /// <summary>Sets the <c>value</c> property (not attribute) so it tracks the signal after user edits.</summary>
    public static Attr Value(Func<string> value) => UI.Attr.Property("value", value);
    public static Attr Value(string value) => UI.Attr.Property("value", () => value);
    public static Attr Checked(Func<bool> value) => UI.Attr.BoolProperty("checked", value);

    // ---- events ----

    public static Listener On(string eventName, Action<DomEvent> handler, ListenerFlags flags = ListenerFlags.None) =>
        new(eventName, handler, flags);

    public static Listener On(string eventName, Action handler, ListenerFlags flags = ListenerFlags.None) =>
        new(eventName, _ => handler(), flags);

    public static Listener OnClick(Action<DomEvent> handler) => new("click", handler);
    public static Listener OnClick(Action handler) => new("click", _ => handler());
    public static Listener OnInput(Action<DomEvent> handler) => new("input", handler);
    public static Listener OnChange(Action<DomEvent> handler) => new("change", handler);
    public static Listener OnKeyDown(Action<DomEvent> handler) => new("keydown", handler);
    public static Listener OnSubmit(Action<DomEvent> handler) => new("submit", handler, ListenerFlags.PreventDefault);
    public static Listener OnDblClick(Action<DomEvent> handler) => new("dblclick", handler);
    public static Listener OnBlur(Action<DomEvent> handler) => new("blur", handler);

    // ---- two-way binding ----

    /// <summary>Two-way binds a text input to a string signal.</summary>
    public static Content[] Bind(Signal<string> signal) => new Content[]
    {
        Value(() => signal.Value),
        OnInput(e => signal.Value = e.Value),
    };

    /// <summary>Two-way binds a checkbox to a bool signal.</summary>
    public static Content[] Bind(Signal<bool> signal) => new Content[]
    {
        Type("checkbox"),
        Checked(() => signal.Value),
        OnChange(e => signal.Value = e.Checked),
    };

    // ---- control flow ----

    public static Show When(Func<bool> condition, Func<Node> render, Func<Node>? otherwise = null) =>
        new(condition, render, otherwise);

    /// <summary>Renders <paramref name="content"/>, or <paramref name="fallback"/> once something inside it throws.</summary>
    public static ErrorBoundary ErrorBoundary(Func<Node> content, Func<BoundaryError, Node> fallback) =>
        new(content, fallback);

    public static For<T, TKey> Each<T, TKey>(Func<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render)
        where TKey : notnull =>
        new(items, key, render);

    public static For<T, TKey> Each<T, TKey>(IReadOnlySignal<IReadOnlyList<T>> items, Func<T, TKey> key, Func<T, Node> render)
        where TKey : notnull =>
        new(() => items.Value, key, render);

    // ---- context & lifecycle ----

    /// <summary>Provides <paramref name="value"/> to everything built after this call in the current component and its descendants.</summary>
    public static void Provide<T>(Context<T> context, T value) => context.Provide(value);

    /// <summary>Builds <paramref name="render"/> in a child scope where <paramref name="context"/> is <paramref name="value"/>.</summary>
    public static Node Provide<T>(Context<T> context, T value, Func<Node> render)
    {
        var scope = new Scope();
        scope.SetContext(context, value);
        return scope.Run(render);
    }

    /// <summary>The nearest provided value of <paramref name="context"/>. Call while building, not inside a reactive lambda.</summary>
    public static T Use<T>(Context<T> context) => context.Use();

    /// <summary>Runs after this component's DOM has been applied by the host. See <see cref="Lifecycle.OnMount"/>.</summary>
    public static void OnMount(Action callback) => Lifecycle.OnMount(callback);

    /// <summary>Runs when the current component/branch (or effect run) is torn down. See <see cref="Lifecycle.OnCleanup"/>.</summary>
    public static void OnCleanup(Action cleanup) => Lifecycle.OnCleanup(cleanup);

    // ---- routing ----

    /// <summary>An in-app link: renders <c>&lt;a href&gt;</c> and routes plain clicks through <see cref="Routing.Router.Current"/>.
    /// Modified/middle clicks fall through to the browser so "open in new tab" keeps working.</summary>
    public static Element Link(string href, params Content[] content)
    {
        var a = new Element("a", Href(href), On("click", e =>
        {
            if (e.IsPlainClick)
            {
                Routing.Router.Current?.Navigate(href);
            }
        }, ListenerFlags.PreventDefaultPlainClick));
        return a.Add(content);
    }

    /// <summary>A <see cref="Link"/> that carries <paramref name="activeClass"/> while its route is active.</summary>
    public static Element NavLink(string href, string activeClass, params Content[] content) =>
        Link(href, Class(() => Routing.Router.Current is { } r && r.IsActive(href) ? activeClass : null)).Add(content);

    public static Routing.Route Route(string pattern, Func<Routing.RouteParams, Node> render) => new(pattern, render);
    public static Routing.Route Route(string pattern, Func<Node> render) => new(pattern, _ => render());

    /// <summary>Renders the first matching route; <paramref name="notFound"/> when none matches.</summary>
    public static Routing.Routes Routes(Func<Node>? notFound, params Routing.Route[] routes) => new(routes, notFound);
    public static Routing.Routes Routes(params Routing.Route[] routes) => new(routes);

    /// <summary>Spreads a content array (e.g. from <see cref="Bind(Signal{string})"/>) into an element.</summary>
    public static Content Spread(params Content[] content) => Content.Of(new SpreadContent(content));

    private sealed class SpreadContent : IContent
    {
        private readonly Content[] content;

        public SpreadContent(Content[] content)
        {
            this.content = content;
        }

        public void ApplyTo(Element parent) => parent.Add(content);
    }
}
