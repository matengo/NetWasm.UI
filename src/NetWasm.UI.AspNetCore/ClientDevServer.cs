using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NetWasm.UI.DevServer;

namespace NetWasm.UI.AspNetCore;

/// <summary>Where the build put the client and how to rebuild it; written by NwUi.AspNetCore.targets.</summary>
internal sealed record ClientDevSettings(string ServerProject, string ClientProject, string ClientDir, string Configuration, bool ReusePrerender)
{
    public static ClientDevSettings? Load(string baseDirectory)
    {
        var file = Path.Combine(baseDirectory, "netwasmui.dev.txt");
        if (!File.Exists(file))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(file))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                values[line[..eq]] = line[(eq + 1)..];
            }
        }

        return values.TryGetValue("ServerProject", out var server)
            && values.TryGetValue("ClientProject", out var client)
            && values.TryGetValue("ClientDir", out var dir)
            && values.TryGetValue("Configuration", out var configuration)
            ? new ClientDevSettings(server, client, dir, configuration,
                !values.TryGetValue("ReusePrerender", out var reuse) || !string.Equals(reuse, "false", StringComparison.OrdinalIgnoreCase))
            : null;
    }
}

/// <summary>
/// Watches the client's sources, republishes it through the NetWasmUIPublishClient target and tells the pages' reload
/// clients (the same script as nwui-dev's) to reload.
/// </summary>
internal sealed class ClientDevServer(ClientDevSettings settings, ILogger logger) : IDisposable
{
    public const string PropertyKey = "NetWasm.UI.DevServer";
    public const string ScriptPath = "/__netwasm_ui/reload-client.js";
    public const string SocketPath = "/__netwasm_ui/reload";

    public static readonly string Script = ReadScript();

    private static readonly string[] ProjectFileExtensions = [".csproj", ".props", ".targets"];

    private readonly ReloadHub hub = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly List<SourceWatcher> watchers = [];
    private bool restoreNext;
    private bool prerenderNext;

    // The pages from the last build that prerendered, by path relative to the client folder. Live reload rebuilds skip
    // prerendering (a few seconds per build) and put these back: the app replaces prerendered HTML when it starts, so
    // only their preload links, whose runtime chunk names are content-hashed, need to be current.
    private Dictionary<string, string>? pages;

    public static string InjectReloadClient(string html)
    {
        var tag = $"<script src=\"{ScriptPath}\"></script>\n";
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? html.Insert(end, tag) : html + tag;
    }

    public async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await hub.HoldAsync(socket, context.RequestAborted);
    }

    public void Start() => _ = Task.Run(StartAsync);

    private async Task StartAsync()
    {
        try
        {
            pages = settings.ReusePrerender ? ReadPages() : null;
            var roots = await WatchRootsAsync();
            lock (watchers)
            {
                if (stopping.IsCancellationRequested)
                {
                    return;
                }

                foreach (var root in roots)
                {
                    watchers.Add(new SourceWatcher(root, OnChangeAsync));
                }
            }

            logger.LogInformation("NetWasm.UI live reload: watching {Roots}", string.Join(", ", roots));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NetWasm.UI live reload could not start.");
        }
    }

    /// <summary>
    /// The client's folder and those of the projects it references. Under dotnet watch, projects the server references
    /// too are left out: a change there rebuilds and restarts the server, which republishes the client itself.
    /// </summary>
    private async Task<List<string>> WatchRootsAsync()
    {
        var client = Path.GetDirectoryName(settings.ClientProject)!;
        var dirs = new List<string> { client };
        dirs.AddRange(await ReferencedProjectDirsAsync(settings.ClientProject));
        if (Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1")
        {
            var server = await ReferencedProjectDirsAsync(settings.ServerProject);
            dirs.RemoveAll(d => d != client && server.Contains(d, StringComparer.OrdinalIgnoreCase));
        }

        // Nested folders are covered by their parent's watcher.
        var full = dirs.Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return full.Where(d => !full.Any(o => o != d && d.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private async Task<List<string>> ReferencedProjectDirsAsync(string project)
    {
        var (code, output) = await RunDotnetAsync(["msbuild", project, "-nologo", "-getItem:ProjectReference", $"-p:Configuration={settings.Configuration}"]);
        var dirs = new List<string>();
        if (code != 0)
        {
            logger.LogWarning("NetWasm.UI live reload could not read the project references of {Project}:\n{Output}", project, output);
            return dirs;
        }

        // No references: MSBuild prints nothing.
        var start = output.IndexOf('{');
        if (start < 0)
        {
            return dirs;
        }

        using var json = JsonDocument.Parse(output[start..]);
        if (json.RootElement.TryGetProperty("Items", out var items) && items.TryGetProperty("ProjectReference", out var references))
        {
            foreach (var reference in references.EnumerateArray())
            {
                if (reference.TryGetProperty("FullPath", out var path) && path.GetString() is { Length: > 0 } file)
                {
                    dirs.Add(Path.GetDirectoryName(file)!);
                }
            }
        }

        return dirs;
    }

    private async Task OnChangeAsync(IReadOnlyCollection<string> changed)
    {
        if (changed.Any(c => ProjectFileExtensions.Contains(Path.GetExtension(c), StringComparer.OrdinalIgnoreCase)))
        {
            restoreNext = true;
        }

        // A changed page template (or project) gives different pages: prerender them again.
        if (changed.Any(c => string.Equals(Path.GetExtension(c), ".html", StringComparison.OrdinalIgnoreCase)))
        {
            prerenderNext = true;
        }

        await gate.WaitAsync(stopping.Token);
        try
        {
            await hub.BroadcastAsync("""{"type":"building"}""");
            var restore = restoreNext;
            var prerender = prerenderNext || restore || pages is not { Count: > 0 };
            restoreNext = false;
            prerenderNext = false;
            var sw = Stopwatch.StartNew();
            List<string> arguments =
            [
                "msbuild", settings.ServerProject, "-t:NetWasmUIPublishClient", "-nologo", "-v:q", "-clp:NoSummary",
                $"-p:Configuration={settings.Configuration}",
                $"-p:NetWasmUIClientDir={settings.ClientDir}",
                $"-p:NetWasmUIRestoreClient={(restore ? "true" : "false")}",
            ];
            if (!prerender)
            {
                arguments.Add("-p:NetWasmUIPrerender=false");
            }

            var (code, output) = await RunDotnetAsync(arguments);
            if (code == 0)
            {
                if (prerender)
                {
                    pages = settings.ReusePrerender ? ReadPages() : null;
                }
                else
                {
                    RestorePages(pages!);
                }

                logger.LogInformation("NetWasm.UI client rebuilt in {Seconds:0.0} s{Pages}; reloading.",
                    sw.Elapsed.TotalSeconds, prerender ? "" : " (prerendered pages reused)");
                await hub.BroadcastAsync("""{"type":"reload"}""");
            }
            else
            {
                restoreNext |= restore;
                prerenderNext |= prerender;
                logger.LogError("NetWasm.UI client build failed:\n{Output}", output);
                await hub.BroadcastAsync(ReloadHub.ErrorMessage(output));
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private const string PrerenderMarker = "<!--nwui:prerender-->";

    private static readonly Regex PreloadBlock = new(@"[ \t]*<!-- nwui:preload -->[\s\S]*?<!-- /nwui:preload -->\r?\n?", RegexOptions.Compiled);

    /// <summary>The client's pages if the build prerendered any, else empty (nothing to keep).</summary>
    private Dictionary<string, string> ReadPages()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(settings.ClientDir))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(settings.ClientDir, "*.html", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(settings.ClientDir, file)] = File.ReadAllText(file);
        }

        return result.Values.Any(html => html.Contains(PrerenderMarker, StringComparison.Ordinal)) ? result : [];
    }

    /// <summary>
    /// Puts back the prerendered pages, and those only prerendering writes (such as the shell), with the preload links
    /// of the page the build just wrote.
    /// </summary>
    private void RestorePages(Dictionary<string, string> saved)
    {
        if (saved.Count == 0)
        {
            return;
        }

        var index = Path.Combine(settings.ClientDir, "index.html");
        var preload = File.Exists(index) ? PreloadBlock.Match(File.ReadAllText(index)) : Match.Empty;
        foreach (var (relative, html) in saved)
        {
            var path = Path.Combine(settings.ClientDir, relative);
            if (!html.Contains(PrerenderMarker, StringComparison.Ordinal) && File.Exists(path))
            {
                continue;
            }

            var page = preload.Success ? PreloadBlock.Replace(html, _ => preload.Value, 1) : html;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, page);
        }
    }

    private async Task<(int Code, string Output)> RunDotnetAsync(IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        // dotnet watch and dotnet run set these for the server; they are not meant for the client's build.
        psi.Environment.Remove("DOTNET_WATCH");
        psi.Environment.Remove("DOTNET_MODIFIABLE_ASSEMBLIES");
        psi.Environment.Remove("DOTNET_STARTUP_HOOKS");

        var sb = new StringBuilder();
        void Append(string? line)
        {
            if (line is not null)
            {
                lock (sb)
                {
                    sb.AppendLine(line);
                }
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for the process itself, not for EOF on its pipes, which a reused MSBuild node may hold.
        await exited.Task;
        await Task.Delay(100);
        lock (sb)
        {
            return (process.ExitCode, sb.ToString());
        }
    }

    private static string ReadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("reload-client.js")
            ?? throw new InvalidOperationException("Embedded resource 'reload-client.js' missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        stopping.Cancel();
        lock (watchers)
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }

            watchers.Clear();
        }
    }
}
