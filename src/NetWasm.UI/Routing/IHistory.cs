using System;

namespace NetWasm.UI.Routing;

/// <summary>
/// The browser history abstraction the <see cref="Router"/> sits on. Paths include query and hash
/// (<c>/todos/3?x=1#top</c>). <see cref="MemoryHistory"/> implements it for tests and servers.
/// </summary>
public interface IHistory
{
    /// <summary>The current path.</summary>
    string Path { get; }

    /// <summary>Pushes (or replaces) an entry. Must not raise <see cref="Changed"/>; the router updates itself.</summary>
    void Navigate(string path, bool replace);

    /// <summary>Raised when the user navigates (back/forward); the argument is the new path.</summary>
    event Action<string>? Changed;
}

/// <summary>In-memory history with a back stack. Used by tests; handy for non-browser hosts.</summary>
public sealed class MemoryHistory : IHistory
{
    private readonly System.Collections.Generic.List<string> stack = new();
    private int index;

    public MemoryHistory(string initialPath = "/")
    {
        stack.Add(initialPath);
    }

    public string Path => stack[index];

    public event Action<string>? Changed;

    public void Navigate(string path, bool replace)
    {
        if (replace)
        {
            stack[index] = path;
            return;
        }

        // Pushing discards any forward entries.
        stack.RemoveRange(index + 1, stack.Count - index - 1);
        stack.Add(path);
        index++;
    }

    /// <summary>Simulates the browser back button.</summary>
    public void Back()
    {
        if (index > 0)
        {
            index--;
            Changed?.Invoke(Path);
        }
    }

    public void Forward()
    {
        if (index < stack.Count - 1)
        {
            index++;
            Changed?.Invoke(Path);
        }
    }
}
