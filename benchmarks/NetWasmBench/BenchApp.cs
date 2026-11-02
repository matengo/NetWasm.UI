using System;
using System.Collections.Generic;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasmBench;

/// <summary>The js-framework-benchmark "keyed" app. <c>benchmarks/solid/src/main.jsx</c> is the same app in SolidJS.</summary>
public sealed class BenchApp : Component
{
    private readonly Signal<IReadOnlyList<Row>> data = new(Array.Empty<Row>());
    private readonly Signal<int> selected = new(0);
    private Selector<int> isSelected = null!;

    protected override Node Build()
    {
        isSelected = new Selector<int>(selected);
        return Div(Class("container"),
            Div(Class("jumbotron"),
                H1("NetWasm.UI keyed"),
                Div(Class("buttons"),
                    Btn("run", "Create 1,000 rows", () => data.Value = Data.Build(1000)),
                    Btn("runlots", "Create 10,000 rows", () => data.Value = Data.Build(10000)),
                    Btn("add", "Append 1,000 rows", Add),
                    Btn("update", "Update every 10th row", Update),
                    Btn("clear", "Clear", () => data.Value = Array.Empty<Row>()),
                    Btn("swaprows", "Swap Rows", Swap))),
            Table(Class("table table-hover table-striped test-data"),
                Tbody(Id("tbody"), Each(data, r => r.Id, RowView))),
            Span(Class("preloadicon glyphicon glyphicon-remove"), Attr("aria-hidden", "true")));
    }

    private static Element Btn(string id, string text, Action action) =>
        Button(Type("button"), Id(id), OnClick(action), text);

    private static readonly Template RowTemplate = new(
        "<tr><td class=\"col-md-1\"><!--$--></td><td class=\"col-md-4\"><a $><!--$--></a></td>" +
        "<td class=\"col-md-1\"><a $><span class=\"glyphicon glyphicon-remove\" aria-hidden=\"true\"></span></a></td><td class=\"col-md-6\"></td></tr>");

    private Node RowView(Row row)
    {
        var id = row.Id;
        return RowTemplate.Clone()
            .With(0, Class(() => isSelected.Is(id) ? "danger" : null))
            .Text(1, id.ToString())
            .With(2, OnClick(() => selected.Value = id))
            .Text(3, () => row.Label.Value)
            .With(4, OnClick(() => Remove(id)))
            .Root;
    }

    private void Add()
    {
        var rows = new List<Row>(data.Value);
        rows.AddRange(Data.Build(1000));
        data.Value = rows;
    }

    private void Update() => Scheduler.Batch(() =>
    {
        var rows = data.Value;
        for (var i = 0; i < rows.Count; i += 10)
        {
            rows[i].Label.Value += " !!!";
        }
    });

    private void Swap()
    {
        if (data.Value.Count > 998)
        {
            var rows = new List<Row>(data.Value);
            (rows[1], rows[998]) = (rows[998], rows[1]);
            data.Value = rows;
        }
    }

    private void Remove(int id)
    {
        var rows = new List<Row>(data.Value);
        rows.RemoveAt(rows.FindIndex(r => r.Id == id));
        data.Value = rows;
    }
}
