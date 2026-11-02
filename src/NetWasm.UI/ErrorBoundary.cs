using System;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI;

/// <summary>
/// Catches errors thrown while building its content, and later from effects, event handlers and
/// <c>OnMount</c> callbacks inside it. The content is then torn down and <c>fallback</c> rendered
/// in its place. Errors thrown by the fallback go to the next boundary out (or <see cref="App.OnError"/>).
/// </summary>
public sealed class ErrorBoundary : Fragment
{
    private readonly Func<Node> content;
    private readonly Func<BoundaryError, Node> fallback;

    public ErrorBoundary(Func<Node> content, Func<BoundaryError, Node> fallback)
    {
        this.content = content;
        this.fallback = fallback;
    }

    protected override void Apply(Element parent)
    {
        var owner = Scope.Current;
        var anchor = new Anchor();
        Renderer.AppendChild(parent.Id, anchor.Id);

        // Bumped to rebuild: on an error reported later (fallback) and on Reset (content).
        var version = new Signal<int>(0);
        Exception? error = null;
        Scope? branch = null;
        Node? current = null;

        void Fail(Exception ex)
        {
            if (error is null)
            {
                error = ex;
                version.Value++;
            }
        }

        void Reset()
        {
            if (error is not null)
            {
                error = null;
                version.Value++;
            }
        }

        _ = new Effect(() =>
        {
            _ = version.Value;
            Tracker.Untracked(() =>
            {
                if (branch is not null)
                {
                    current?.Remove();
                    branch.Dispose();
                    current = null;
                    branch = null;
                }

                if (error is null)
                {
                    branch = new Scope(owner) { ErrorHandler = Fail };
                    try
                    {
                        current = branch.Run(content);
                    }
                    catch (Exception ex)
                    {
                        branch.Dispose();
                        branch = null;
                        error = ex;
                    }
                }

                if (error is not null)
                {
                    branch = new Scope(owner);
                    var info = new BoundaryError(error, Reset);
                    current = branch.Run(() => fallback(info));
                }

                Renderer.InsertBefore(anchor.Id, current!.Id);
            });
        });
    }
}

/// <summary>The error an <see cref="ErrorBoundary"/> caught, passed to its fallback.</summary>
public sealed class BoundaryError
{
    private readonly Action reset;

    internal BoundaryError(Exception exception, Action reset)
    {
        Exception = exception;
        this.reset = reset;
    }

    public Exception Exception { get; }

    public string Message => Exception.Message;

    /// <summary>Clears the error and builds the content again.</summary>
    public void Reset() => reset();
}
