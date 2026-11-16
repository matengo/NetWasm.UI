using System;

namespace NetWasm.UI.Dom;

/// <summary>The boundary to the real (or fake) DOM. One call per flush.</summary>
public interface IDomHost
{
    /// <summary>
    /// Applies an encoded batch of ops. See <see cref="Op"/> for the format. <paramref name="ops"/> is a view
    /// of the renderer's buffer and is only valid during the call; copy it to keep it.
    /// </summary>
    void ApplyBatch(ReadOnlySpan<byte> ops);
}

/// <summary>
/// Optional synchronous DOM reads, implemented by hosts that can answer them (the browser host does). The renderer
/// flushes its pending ops before every read. Use the public wrappers: <see cref="Element.GetRect"/>,
/// <see cref="Element.Contains"/>, <see cref="Html.ActiveElementId"/> and <see cref="Html.Viewport"/>.
/// </summary>
public interface IDomQueries
{
    /// <summary><c>getBoundingClientRect()</c> of node <paramref name="id"/>; empty when it does not exist.</summary>
    Rect GetRect(int id);

    /// <summary>The node id of <c>document.activeElement</c>, or 0 when it is not a node the renderer created.</summary>
    int ActiveElementId();

    /// <summary>True when node <paramref name="childId"/> is <paramref name="parentId"/> or inside it.</summary>
    bool Contains(int parentId, int childId);

    /// <summary>The size of the viewport (<c>innerWidth</c>/<c>innerHeight</c>) at position 0, 0.</summary>
    Rect Viewport();
}

/// <summary>A rectangle in CSS pixels, relative to the viewport.</summary>
public readonly struct Rect : IEquatable<Rect>
{
    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>Reads <c>[x, y, width, height]</c>; a shorter array gives zeros.</summary>
    public static Rect FromArray(double[]? values) =>
        values is { Length: >= 4 } v ? new Rect(v[0], v[1], v[2], v[3]) : default;

    public bool Equals(Rect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is Rect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);

    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
}
