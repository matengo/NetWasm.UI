namespace NetWasm.UI.Primitives;

/// <summary>
/// Keyboard navigation for a roving-tabindex group (tabs, menus, toolbars): only the active item has
/// <c>tabindex="0"</c>, and the arrow keys, Home and End move between items.
/// </summary>
public static class RovingFocus
{
    /// <summary>
    /// The index to move to from <paramref name="from"/> for <paramref name="key"/>, or -1 when the key does not
    /// navigate. Vertical groups use ArrowUp/ArrowDown, horizontal ones ArrowLeft/ArrowRight; both wrap around.
    /// </summary>
    public static int Move(string? key, int from, int count, bool vertical = false)
    {
        if (count <= 0)
        {
            return -1;
        }

        var next = vertical ? "ArrowDown" : "ArrowRight";
        var previous = vertical ? "ArrowUp" : "ArrowLeft";
        if (key == next)
        {
            return (from + 1) % count;
        }

        if (key == previous)
        {
            return (from + count - 1) % count;
        }

        return key switch
        {
            "Home" => 0,
            "End" => count - 1,
            _ => -1,
        };
    }
}
