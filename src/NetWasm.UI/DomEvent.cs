namespace NetWasm.UI;

/// <summary>Event data delivered from the browser. The payload depends on the event type (see dom.mjs).</summary>
public readonly struct DomEvent
{
    public DomEvent(string type, string payload)
    {
        Type = type;
        Payload = payload;
    }

    public string Type { get; }

    /// <summary>Raw payload: input value for input/change, key for keyboard events, otherwise empty.</summary>
    public string Payload { get; }

    /// <summary>The input value for input/change events.</summary>
    public string Value => Payload;

    /// <summary>The checkbox state for change events on checkboxes.</summary>
    public bool Checked => Payload == "true";

    /// <summary>The key for keyboard events.</summary>
    public string Key => Payload;
}
