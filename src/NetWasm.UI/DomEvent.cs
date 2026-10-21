using System;
using System.Text;

namespace NetWasm.UI;

/// <summary>Modifier keys held while an event fired.</summary>
[Flags]
public enum EventModifiers : byte
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>
/// Event data delivered from the browser as one small binary record (see <c>payloadFor</c> in dom.mjs).
/// All fields are always present; irrelevant ones are zero/empty. Layout (little endian):
/// <c>u8 flags, i32 button, i32 clientX, i32 clientY, str key, str value</c> where
/// flags = modifiers (bits 0–3) | checked (16) | repeat (32) and str = u32 length + UTF-8 bytes.
/// </summary>
public readonly struct DomEvent
{
    private const byte CheckedFlag = 16;
    private const byte RepeatFlag = 32;
    private const byte ModifierMask = 15;

    public DomEvent(
        string type,
        string value = "",
        string key = "",
        bool isChecked = false,
        EventModifiers modifiers = EventModifiers.None,
        int button = 0,
        int clientX = 0,
        int clientY = 0,
        bool repeat = false)
    {
        Type = type;
        Value = value;
        Key = key;
        Checked = isChecked;
        Modifiers = modifiers;
        Button = button;
        ClientX = clientX;
        ClientY = clientY;
        Repeat = repeat;
    }

    /// <summary>DOM event type, e.g. <c>click</c>.</summary>
    public string Type { get; }

    /// <summary>The input value for input/change events.</summary>
    public string Value { get; }

    /// <summary>The key for keyboard events.</summary>
    public string Key { get; }

    /// <summary>The checkbox state for change events on checkboxes.</summary>
    public bool Checked { get; }

    public EventModifiers Modifiers { get; }

    /// <summary>Mouse button (0 = primary) for mouse/pointer events.</summary>
    public int Button { get; }

    public int ClientX { get; }
    public int ClientY { get; }

    /// <summary>True for auto-repeated keydown events.</summary>
    public bool Repeat { get; }

    public bool Ctrl => (Modifiers & EventModifiers.Ctrl) != 0;
    public bool Shift => (Modifiers & EventModifiers.Shift) != 0;
    public bool Alt => (Modifiers & EventModifiers.Alt) != 0;
    public bool Meta => (Modifiers & EventModifiers.Meta) != 0;

    /// <summary>True for a primary-button click with no modifier keys, i.e. plain in-page navigation intent.</summary>
    public bool IsPlainClick => Button == 0 && Modifiers == EventModifiers.None;

    /// <summary>Decodes a payload produced by the host. An empty payload yields an event with default fields.</summary>
    public static DomEvent Decode(string type, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return new DomEvent(type);
        }

        var pos = 0;
        var flags = payload[pos++];
        var button = ReadInt(payload, ref pos);
        var clientX = ReadInt(payload, ref pos);
        var clientY = ReadInt(payload, ref pos);
        var key = ReadString(payload, ref pos);
        var value = ReadString(payload, ref pos);

        return new DomEvent(
            type,
            value,
            key,
            (flags & CheckedFlag) != 0,
            (EventModifiers)(flags & ModifierMask),
            button,
            clientX,
            clientY,
            (flags & RepeatFlag) != 0);
    }

    /// <summary>Encodes this event in the wire format. Used by tests and fake hosts; the browser encodes in JS.</summary>
    public byte[] Encode()
    {
        var keyBytes = Encoding.UTF8.GetBytes(Key);
        var valueBytes = Encoding.UTF8.GetBytes(Value);
        var bytes = new byte[1 + 4 * 3 + 4 + keyBytes.Length + 4 + valueBytes.Length];
        var pos = 0;
        bytes[pos++] = (byte)((byte)Modifiers | (Checked ? CheckedFlag : 0) | (Repeat ? RepeatFlag : 0));
        WriteInt(bytes, ref pos, Button);
        WriteInt(bytes, ref pos, ClientX);
        WriteInt(bytes, ref pos, ClientY);
        WriteInt(bytes, ref pos, keyBytes.Length);
        Array.Copy(keyBytes, 0, bytes, pos, keyBytes.Length);
        pos += keyBytes.Length;
        WriteInt(bytes, ref pos, valueBytes.Length);
        Array.Copy(valueBytes, 0, bytes, pos, valueBytes.Length);
        return bytes;
    }

    private static int ReadInt(byte[] b, ref int pos)
    {
        var v = b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);
        pos += 4;
        return v;
    }

    private static string ReadString(byte[] b, ref int pos)
    {
        var len = ReadInt(b, ref pos);
        var s = len == 0 ? string.Empty : Encoding.UTF8.GetString(b, pos, len);
        pos += len;
        return s;
    }

    private static void WriteInt(byte[] b, ref int pos, int value)
    {
        b[pos++] = (byte)value;
        b[pos++] = (byte)(value >> 8);
        b[pos++] = (byte)(value >> 16);
        b[pos++] = (byte)(value >> 24);
    }
}
