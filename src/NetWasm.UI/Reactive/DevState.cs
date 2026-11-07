using System;
using System.Text;

namespace NetWasm.UI.Reactive;

/// <summary>
/// Keeps <c>int</c>, <c>bool</c> and <c>string</c> signal values across a dev-server reload.
/// </summary>
/// <remarks>
/// Only Debug builds use this: the source generator emits a module initializer that calls
/// <c>DevReload.Install</c> (NetWasm.UI.Browser), which sets <see cref="Tracker"/> before any signal exists.
/// Release builds never reference <see cref="Track"/>, so none of this ends up in their Wasm.
/// Signals are numbered in creation order. Before a reload the dev server asks for <see cref="Serialize"/>;
/// after it, <see cref="Enable"/> gets that text back, and a signal takes its old value only when its
/// number, type and initial value all match (so an edit that adds, removes or reorders signals resets
/// the ones that no longer line up). Plain arrays on purpose: each new generic instantiation costs Wasm.
/// </remarks>
internal static class DevState
{
    private const int TrackLimit = 10_000;

    // A tracked signal (Signal set) or a value saved before the reload (Current set).
    private sealed class Record
    {
        public int Index;
        public char Tag;
        public string Initial = string.Empty;
        public string Current = string.Empty;
        public object? Signal;
    }

    /// <summary>Called by every <see cref="Signal{T}"/> constructor; null unless dev state is on.</summary>
    internal static Action<object>? Tracker;

    private static int next;
    private static Record[]? entries;
    private static int entryCount;
    private static Record[]? saved;
    private static int savedCount;
    private static int savedCursor;

    /// <summary>Turns dev state on with the <paramref name="state"/> saved before the reload.</summary>
    internal static void Enable(string state)
    {
        Parse(state);
        Tracker = Track;
    }

    private static void Track(object signal)
    {
        var index = next++;
        char tag;
        string initial;
        switch (signal)
        {
            case Signal<int> i:
                tag = 'i';
                initial = FormatInt(i.Peek());
                break;
            case Signal<bool> b:
                tag = 'b';
                initial = b.Peek() ? "1" : "0";
                break;
            case Signal<string> s:
                tag = 's';
                initial = EncodeString(s.Peek());
                break;
            default:
                return;
        }

        var entry = new Record { Index = index, Tag = tag, Initial = initial, Signal = signal };
        entries = Add(entries, ref entryCount, entry);
        Restore(entry);
        if (entryCount >= TrackLimit)
        {
            Tracker = null;
        }
    }

    /// <summary>Test hook: turns dev state off and forgets everything.</summary>
    internal static void Reset()
    {
        Tracker = null;
        next = 0;
        entries = null;
        entryCount = 0;
        saved = null;
        savedCount = 0;
        savedCursor = 0;
    }

    internal static string Serialize()
    {
        var sb = new StringBuilder();
        for (var k = 0; k < entryCount; k++)
        {
            var e = entries![k];
            var current = e.Signal switch
            {
                Signal<int> i => FormatInt(i.Peek()),
                Signal<bool> b => b.Peek() ? "1" : "0",
                Signal<string> s => EncodeString(s.Peek()),
                _ => e.Initial,
            };
            if (current == e.Initial)
            {
                continue;
            }

            sb.Append(FormatInt(e.Index)).Append(';').Append(e.Tag);
            Write(sb, e.Initial);
            Write(sb, current);
        }

        return sb.ToString();
    }

    private static Record[] Add(Record[]? array, ref int count, Record item)
    {
        if (array is null || count == array.Length)
        {
            var grown = new Record[array is null ? 16 : array.Length * 2];
            for (var k = 0; k < count; k++)
            {
                grown[k] = array![k];
            }

            array = grown;
        }

        array[count++] = item;
        return array;
    }

    private static void Restore(Record e)
    {
        while (savedCursor < savedCount && saved![savedCursor].Index < e.Index)
        {
            savedCursor++;
        }

        if (savedCursor >= savedCount)
        {
            return;
        }

        var s = saved![savedCursor];
        if (s.Index != e.Index || s.Tag != e.Tag || s.Initial != e.Initial)
        {
            return;
        }

        savedCursor++;
        switch (e.Signal)
        {
            case Signal<int> i when TryParseInt(s.Current, out var n):
                i.Value = n;
                break;
            case Signal<bool> b:
                b.Value = s.Current == "1";
                break;
            case Signal<string> str:
                str.Value = DecodeString(s.Current)!;
                break;
        }
    }

    // null and "" must stay distinct: "-" is null, "+text" is a string.
    private static string EncodeString(string? value) => value is null ? "-" : "+" + value;

    private static string? DecodeString(string encoded) => encoded.Length > 0 && encoded[0] == '+' ? encoded.Substring(1) : null;

    private static void Write(StringBuilder sb, string value) =>
        sb.Append(FormatInt(value.Length)).Append(':').Append(value);

    // Records: <index>;<tag><len>:<initial><len>:<current>. Anything malformed ends the parse.
    private static void Parse(string text)
    {
        saved = null;
        savedCount = 0;
        savedCursor = 0;
        var p = 0;
        while (p < text.Length)
        {
            if (!ReadInt(text, ref p, ';', out var index) || p >= text.Length)
            {
                return;
            }

            var tag = text[p++];
            if (!ReadString(text, ref p, out var initial) || !ReadString(text, ref p, out var current))
            {
                return;
            }

            saved = Add(saved, ref savedCount, new Record { Index = index, Tag = tag, Initial = initial, Current = current });
        }
    }

    private static bool ReadString(string text, ref int p, out string value)
    {
        value = string.Empty;
        if (!ReadInt(text, ref p, ':', out var length) || length < 0 || p + length > text.Length)
        {
            return false;
        }

        value = text.Substring(p, length);
        p += length;
        return true;
    }

    private static bool ReadInt(string text, ref int p, char end, out int value)
    {
        var start = p;
        while (p < text.Length && text[p] != end)
        {
            p++;
        }

        if (p >= text.Length || !TryParseInt(text.Substring(start, p - start), out value))
        {
            value = 0;
            return false;
        }

        p++;
        return true;
    }

    // ASCII only, like FormatInt: int.Parse and int.ToString pull in culture data.
    private static bool TryParseInt(string s, out int value)
    {
        value = 0;
        var i = s.Length > 0 && s[0] == '-' ? 1 : 0;
        if (i >= s.Length)
        {
            return false;
        }

        long n = 0;
        for (var k = i; k < s.Length; k++)
        {
            var c = s[k];
            if (c < '0' || c > '9')
            {
                return false;
            }

            n = n * 10 + (c - '0');
            if (n > 2147483648L)
            {
                return false;
            }
        }

        n = i == 1 ? -n : n;
        if (n > int.MaxValue)
        {
            return false;
        }

        value = (int)n;
        return true;
    }

    private static string FormatInt(int value)
    {
        var buffer = new char[11];
        var p = buffer.Length;
        var n = value < 0 ? -(long)value : value;
        do
        {
            buffer[--p] = (char)('0' + (int)(n % 10));
            n /= 10;
        }
        while (n != 0);

        if (value < 0)
        {
            buffer[--p] = '-';
        }

        return new string(buffer, p, buffer.Length - p);
    }
}
