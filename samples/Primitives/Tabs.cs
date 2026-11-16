using System.Collections.Generic;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Primitives;

/// <summary>
/// WAI-ARIA tabs with roving focus: only the selected tab is in the tab order, and the arrow keys,
/// Home and End move both the selection and the focus.
/// </summary>
public sealed class Tabs : Component
{
    public IReadOnlyList<string> Labels { get; set; } = new string[0];

    /// <summary>The panel for a tab index.</summary>
    public Slot<int>? Panel { get; set; }

    public Signal<int> Selected { get; set; } = new(0);

    protected override Node Build()
    {
        var id = UseId("tabs");
        var count = Labels.Count;
        var tabs = new Element[count];
        var list = Div(Attr("role", "tablist"), Splat(Attributes));
        for (var i = 0; i < count; i++)
        {
            var index = i;
            tabs[i] = Button(
                Type("button"),
                Id(id + "-tab-" + index.ToString()),
                Attr("role", "tab"),
                Attr("aria-controls", id + "-panel-" + index.ToString()),
                Attr("aria-selected", () => Selected.Value == index ? "true" : "false"),
                Attr("tabindex", () => Selected.Value == index ? "0" : "-1"),
                OnClick(() => Selected.Value = index),
                OnKeyDown(e => Move(e, index)),
                Text(Labels[i]));
            list.Add(tabs[i]);
        }

        void Move(DomEvent e, int from)
        {
            var to = e.Key switch
            {
                "ArrowRight" => (from + 1) % count,
                "ArrowLeft" => (from + count - 1) % count,
                "Home" => 0,
                "End" => count - 1,
                _ => -1,
            };
            if (to >= 0)
            {
                Selected.Value = to;
                tabs[to].Focus();
            }
        }

        var root = Div(Class("tabs"), list);
        for (var i = 0; i < count; i++)
        {
            var index = i;
            root.Add(Div(
                Id(id + "-panel-" + index.ToString()),
                Attr("role", "tabpanel"),
                Attr("aria-labelledby", id + "-tab-" + index.ToString()),
                Attr("tabindex", "0"),
                Attr("hidden", () => Selected.Value == index ? null : ""),
                Panel is null ? Spread() : Panel.With(index)));
        }

        return root;
    }
}
