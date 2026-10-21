using System;
using System.Text;

namespace NetWasm.UI.Dom;

/// <summary>Growable little-endian byte buffer that encodes render ops.</summary>
public sealed class BatchWriter
{
    private byte[] buffer;
    private int length;

    public BatchWriter(int initialCapacity = 1024)
    {
        buffer = new byte[initialCapacity];
    }

    public int Length => length;
    public bool IsEmpty => length == 0;
    public int OpCount { get; private set; }

    public void Clear()
    {
        length = 0;
        OpCount = 0;
    }

    /// <summary>Returns a right-sized copy of the encoded ops and clears the writer.</summary>
    public byte[] Take()
    {
        var result = new byte[length];
        Array.Copy(buffer, result, length);
        Clear();
        return result;
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

    public void String(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        Ensure(4 + byteCount);
        Int(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, length);
        length += byteCount;
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
