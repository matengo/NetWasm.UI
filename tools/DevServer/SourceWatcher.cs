using System.Security.Cryptography;

namespace NetWasm.UI.DevServer;

/// <summary>
/// Debounced recursive file watcher that ignores build output and
/// no-op writes (same content hash as last time).
/// </summary>
internal sealed class SourceWatcher : IDisposable
{
    private static readonly string[] Extensions = [".cs", ".nwui", ".csproj", ".props", ".targets", ".mjs", ".js", ".html", ".css", ".json"];
    private static readonly string[] IgnoredSegments = ["bin", "obj", ".git", ".vs", "node_modules", "TestResults"];

    private readonly FileSystemWatcher watcher;
    private readonly Func<IReadOnlyCollection<string>, Task> onChange;
    private readonly Lock gate = new();
    private readonly Dictionary<string, byte[]> hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? pending;

    public SourceWatcher(string root, Func<IReadOnlyCollection<string>, Task> onChange)
    {
        this.onChange = onChange;
        SeedHashes(root);
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

    private void SeedHashes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (IsInteresting(file) && TryHash(file) is { } h)
            {
                hashes[file] = h;
            }
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsInteresting(e.FullPath))
        {
            return;
        }

        lock (gate)
        {
            pendingPaths.Add(e.FullPath);
            pending?.Cancel();
            pending = new CancellationTokenSource();
            var token = pending.Token;
            _ = Task.Delay(300, token).ContinueWith(
                _ => Flush(),
                token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }

    private Task Flush()
    {
        List<string> changed = [];
        lock (gate)
        {
            foreach (var path in pendingPaths)
            {
                if (HasContentChanged(path))
                {
                    changed.Add(path);
                }
            }

            pendingPaths.Clear();
        }

        return changed.Count == 0 ? Task.CompletedTask : onChange(changed);
    }

    /// <summary>Updates the stored hash and reports whether the content really differs.</summary>
    private bool HasContentChanged(string path)
    {
        var now = TryHash(path);
        hashes.TryGetValue(path, out var before);

        if (now is null)
        {
            return hashes.Remove(path); // deleted: only a change if we knew about it
        }

        hashes[path] = now;
        return before is null || !before.AsSpan().SequenceEqual(now);
    }

    private static byte[]? TryHash(string path)
    {
        // The editor may still hold the file; retry briefly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return SHA256.HashData(stream);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) { Thread.Sleep(50); }
        }

        return null;
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
