using System.Runtime.InteropServices.JavaScript;
using NetWasm.UI;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace Interop;

/// <summary>
/// Wraps the JS meter in a component: the framework renders an empty container, the library fills it
/// after mount, an effect pushes signal changes to it, and cleanup destroys it.
/// </summary>
public sealed class Meter : Component
{
    private static int instances;

    public Signal<int> Level { get; set; } = null!;

    public int Max { get; set; } = 10;

    protected override Node Build()
    {
        var id = "meter-" + (++instances).ToString();
        var level = Level;
        JSObject? meter = null;
        JSSubscription? changes = null;

        // The container exists in the document only after the first flush.
        OnMount(() =>
        {
            meter = MeterJs.Create("#" + id, Max);
            MeterJs.SetValue(meter, level.Peek());
            changes = MeterJs.OnChange(meter, value => Scheduler.Batch(() => level.Value = value));
        });

        // Runs now (meter is still null) and again whenever Level changes.
        new Effect(() =>
        {
            var value = level.Value;
            if (meter is not null)
            {
                MeterJs.SetValue(meter, value);
            }
        });

        OnCleanup(() =>
        {
            changes?.Dispose();
            if (meter is not null)
            {
                MeterJs.Destroy(meter);
                meter.Dispose();
            }
        });

        // No children: the library owns everything inside this element.
        return Div(Id(id), Class("meter"));
    }
}