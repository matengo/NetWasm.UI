using System;
using System.Text;

namespace NetWasm.UI.Dom;

/// <summary>Growable little-endian byte buffer that encodes render ops.</summary>
internal sealed class BatchWriter
{
    // A buffer above this size is not kept for a batch that uses less than an eighth of it: one large render (a
    // big list) would otherwise leave two buffers of its size behind for every later batch.
    internal const int KeepAtMost = 64 * 1024;

    private readonly int initialCapacity;
    private byte[] buffer;
    private byte[]? spare;
    private int length;
    private int detachedCount;
    // Where the operands of the last ReleaseBlock op start, while that op is still the last one in the batch.
    private int openRelease = -1;

    public BatchWriter(int initialCapacity = 1024)
    {
        this.initialCapacity = initialCapacity;
        buffer = new byte[initialCapacity];
    }

    /// <summary>The capacity of the buffer ops are written into (for tests).</summary>
    internal int Capacity => buffer.Length;

    public int Length => length;
    public bool IsEmpty => length == 0;
    public int OpCount { get; private set; }

    public void Clear()
    {
        length = 0;
        OpCount = 0;
        openRelease = -1;
    }

    /// <summary>
    /// Releases the ids <paramref name="first"/> .. <paramref name="first"/> + <paramref name="count"/> - 1 with
    /// one op. A block that touches the ids of the op before it, on either side, extends that op: the rows of a
    /// list got their blocks one after the other, so a list that is cleared releases all of them with one op.
    /// </summary>
    public void ReleaseBlock(int first, int count)
    {
        if (openRelease >= 0 && openRelease + 8 == length)
        {
            var runFirst = ReadInt(openRelease);
            var runCount = ReadInt(openRelease + 4);
            if (first == runFirst + runCount)
            {
                WriteInt(openRelease + 4, runCount + count);
                return;
            }

            if (first + count == runFirst)
            {
                WriteInt(openRelease, first);
                WriteInt(openRelease + 4, runCount + count);
                return;
            }
        }

        Op(Dom.Op.ReleaseBlock);
        openRelease = length;
        Int(first);
        Int(count);
    }

    private int ReadInt(int at) => buffer[at] | (buffer[at + 1] << 8) | (buffer[at + 2] << 16) | (buffer[at + 3] << 24);

    private void WriteInt(int at, int value)
    {
        buffer[at] = (byte)value;
        buffer[at + 1] = (byte)(value >> 8);
        buffer[at + 2] = (byte)(value >> 16);
        buffer[at + 3] = (byte)(value >> 24);
    }

    /// <summary>Returns a right-sized copy of the encoded ops and clears the writer.</summary>
    public byte[] Take()
    {
        var result = new byte[length];
        Array.Copy(buffer, result, length);
        Clear();
        return result;
    }

    /// <summary>
    /// Hands out the buffer holding the encoded ops (valid bytes: <paramref name="count"/>) without copying,
    /// and continues on another buffer, so ops written while the caller still reads the detached one
    /// (an event dispatched re-entrantly) cannot overwrite it. Give it back with <see cref="Return"/>.
    /// </summary>
    public byte[] Detach(out int count)
    {
        var detached = buffer;
        count = length;
        detachedCount = length;
        buffer = spare ?? new byte[Oversized(detached.Length, count) ? SizeFor(count) : detached.Length];
        spare = null;
        Clear();
        return detached;
    }

    /// <summary>Returns a buffer obtained from <see cref="Detach"/> for reuse.</summary>
    public void Return(byte[] detached)
    {
        if (Oversized(detached.Length, detachedCount))
        {
            return;
        }

        if (spare is null || spare.Length < detached.Length)
        {
            spare = detached;
        }
    }

    public void Op(Op op)
    {
        OpCount++;
        Ensure(1);
        buffer[length++] = (byte)op;
    }

    public void Int(int value)
    {
        Ensure(4);
        buffer[length++] = (byte)value;
        buffer[length++] = (byte)(value >> 8);
        buffer[length++] = (byte)(value >> 16);
        buffer[length++] = (byte)(value >> 24);
    }

    public void Byte(byte value)
    {
        Ensure(1);
        buffer[length++] = value;
    }

    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public void String(string? value)
    {
        // Null renders as empty; checked before writing so a throwing encoder can never leave half an op behind.
        value ??= string.Empty;

        // ASCII fast path: one pass, no encoder call. Falls back to UTF-8 at the first non-ASCII char.
        var n = value.Length;
        Ensure(4 + n);
        var p = length + 4;
        var i = 0;
        while (i < n && value[i] < 0x80)
        {
            buffer[p + i] = (byte)value[i];
            i++;
        }

        if (i == n)
        {
            Int(n);
            length += n;
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        Ensure(4 + byteCount);
        Int(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, length);
        length += byteCount;
    }

    private static bool Oversized(int capacity, int used) => capacity > KeepAtMost && used < capacity / 8;

    // Room for twice what the last batch used, at least the initial capacity, in powers of two.
    private int SizeFor(int used)
    {
        var size = initialCapacity;
        while (size < used * 2)
        {
            size *= 2;
        }

        return size;
    }

    private void Ensure(int additional)
    {
        var required = length + additional;
        if (required <= buffer.Length)
        {
            return;
        }

        var newSize = buffer.Length * 2;
        while (newSize < required)
        {
            newSize *= 2;
        }

        Array.Resize(ref buffer, newSize);
    }
}
