namespace NetWasm.UI.DevServer;

/// <summary>Debounced recursive file watcher that ignores build output.</summary>
internal sealed class SourceWatcher : IDisposable
{
    private static readonly string[] Extensions = [".cs", ".csproj", ".props", ".targets", ".mjs", ".js", ".html", ".css", ".json"];
    private static readonly string[] IgnoredSegments = ["bin", "obj", ".git", ".vs", "node_modules", "TestResults"];

    private readonly FileSystemWatcher watcher;
    private readonly Func<string, Task> onChange;
    private readonly Lock gate = new();
    private CancellationTokenSource? pending;
    private string? lastPath;

    public SourceWatcher(string root, Func<string, Task> onChange)
    {
        this.onChange = onChange;
        watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Deleted += OnEvent;
        watcher.Renamed += OnEvent;
        watcher.EnableRaisingEvents = true;
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsInteresting(e.FullPath))
        {
            return;
        }

        lock (gate)
        {
            pending?.Cancel();
            pending = new CancellationTokenSource();
            lastPath = e.FullPath;
            var token = pending.Token;
            _ = Task.Delay(300, token).ContinueWith(
                _ => onChange(lastPath!),
                token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }

    private static bool IsInteresting(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(s => IgnoredSegments.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        watcher.Dispose();
        pending?.Cancel();
    }
}
