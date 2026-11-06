using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Site.Code;

/// <summary>
/// Fetches a source file that is published next to the site and shows it highlighted.
/// Uses Resource + Suspense + ErrorBoundary, like an app loading data.
/// </summary>
public sealed class SourceView : Component
{
    /// <summary>Path relative to the site root, e.g. <c>source/CounterDemo.nwui.txt</c>.</summary>
    public string Path { get; set; } = "";

    public string Lang { get; set; } = "nwui";

    public string Caption { get; set; } = "";

    protected override Node Build()
    {
        var path = Path;
        var lang = Lang;
        var caption = Caption;
        return Div(
            Class("source-view"),
            ErrorBoundary(
                () => Div(Suspense(
                    () => new Loader { Path = path, Lang = lang, Caption = caption }.Render(),
                    () => Div(Class("code code-loading"), P("Loading source…")))),
                error => Div(
                    Class("code code-loading"),
                    P("Could not load the source: ", error.Message, " "),
                    Button(OnClick(() => error.Reset()), "Retry"))));
    }

    private sealed class Loader : Component
    {
        public string Path { get; set; } = "";

        public string Lang { get; set; } = "";

        public string Caption { get; set; } = "";

        protected override Node Build()
        {
            var path = Path;
            var source = Resource.Create(_ => SiteJs.FetchText(path));
            return Div(When(
                () => source.Value is not null,
                () => new CodeBlock { Source = source.Latest!, Lang = Lang, Caption = Caption }.Render()));
        }
    }
}
