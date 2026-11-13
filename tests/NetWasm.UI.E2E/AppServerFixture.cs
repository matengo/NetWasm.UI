using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace NetWasm.UI.E2E;

/// <summary>
/// Publishes and serves a sample (default <c>samples/Counter</c>) with the dev server (once per test run) and launches the browser named by <c>E2E_BROWSER</c> (default Chromium).
/// Set <c>NETWASM_UI_E2E_URL</c> (e.g. http://localhost:8765) to test an already running server instead.
/// </summary>
public class AppServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    // Dev servers publish shared projects; start them one at a time to avoid concurrent builds.
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private readonly StringBuilder log = new();
    private readonly string sample;
    private Process? server;
    private IPlaywright? playwright;

    public AppServerFixture()
        : this("Counter")
    {
    }

    protected AppServerFixture(string sample) => this.sample = sample;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var external = sample == "Counter" ? Environment.GetEnvironmentVariable("NETWASM_UI_E2E_URL") : null;
        await StartGate.WaitAsync();
        try
        {
            BaseUrl = string.IsNullOrEmpty(external) ? await StartServerAsync() : external.TrimEnd('/');
        }
        finally
        {
            StartGate.Release();
        }

        playwright = await Playwright.CreateAsync();
        var browserName = BrowserName;
        try
        {
            var options = new BrowserTypeLaunchOptions { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" };
            Browser = await playwright[browserName].LaunchAsync(options);
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                $"{browserName} for Playwright is not installed. Run: pwsh tests/NetWasm.UI.E2E/bin/Debug/net10.0/playwright.ps1 install {browserName}",
                ex);
        }
    }

    /// <summary>The Playwright browser to run in: <c>E2E_BROWSER</c> = chromium (default), firefox or webkit.</summary>
    public static string BrowserName =>
        Environment.GetEnvironmentVariable("E2E_BROWSER") is { Length: > 0 } name ? name : "chromium";

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        if (server is { HasExited: false })
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }

        server?.Dispose();
    }

    private async Task<string> StartServerAsync()
    {
        var root = FindRepoRoot();
        var port = FreePort();
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "run", "--project", Path.Combine("tools", "DevServer"), "--", Path.Combine("samples", sample), "--port", port.ToString(), "--no-watch" })
        {
            psi.ArgumentList.Add(a);
        }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server = new Process { StartInfo = psi, EnableRaisingEvents = true };
        server.OutputDataReceived += (_, e) => OnLine(e.Data, ready);
        server.ErrorDataReceived += (_, e) => OnLine(e.Data, ready);
        server.Exited += (_, _) => ready.TrySetException(new InvalidOperationException($"Dev server exited ({server.ExitCode}).\n{Log()}"));
        server.Start();
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();

        var done = await Task.WhenAny(ready.Task, Task.Delay(StartupTimeout));
        if (done != ready.Task)
        {
            throw new TimeoutException($"Dev server did not start within {StartupTimeout}.\n{Log()}");
        }

        await ready.Task;
        return $"http://localhost:{port}";
    }

    private void OnLine(string? line, TaskCompletionSource ready)
    {
        if (line is null)
        {
            return;
        }

        lock (log)
        {
            log.AppendLine(line);
        }

        if (line.StartsWith("Listening:", StringComparison.Ordinal))
        {
            ready.TrySetResult();
        }
    }

    private string Log()
    {
        lock (log)
        {
            return log.ToString();
        }
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetWasm.UI.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find NetWasm.UI.slnx above " + AppContext.BaseDirectory);
    }
}

/// <summary>Serves <c>samples/Widgets</c>: one Wasm module instantiated several times on one page.</summary>
public sealed class WidgetsServerFixture : AppServerFixture
{
    public WidgetsServerFixture()
        : base("Widgets")
    {
    }
}

/// <summary>Serves <c>samples/Fetch</c>: HttpClient + System.Text.Json with Resource and Suspense.</summary>
public sealed class FetchServerFixture : AppServerFixture
{
    public FetchServerFixture()
        : base("Fetch")
    {
    }
}

/// <summary>Serves <c>samples/Interop</c>: a JS library wrapped in a component via [JSImport].</summary>
public sealed class InteropServerFixture : AppServerFixture
{
    public InteropServerFixture()
        : base("Interop")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppServerFixture>, ICollectionFixture<WidgetsServerFixture>, ICollectionFixture<FetchServerFixture>, ICollectionFixture<InteropServerFixture>
{
    public const string Name = "app";
}

/// <summary>Base class: a fresh browser context per test, failing the test on any console error or page error.</summary>
[Collection(AppCollection.Name)]
public abstract class PageTest : IAsyncLifetime
{
    private readonly List<string> errors = new();
    private readonly List<string> allowed = new();
    private IBrowserContext context = null!;

    protected PageTest(AppServerFixture app) => App = app;

    protected AppServerFixture App { get; }

    protected IPage Page { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        context = await App.Browser.NewContextAsync(new() { BaseURL = App.BaseUrl });
        context.SetDefaultTimeout(15_000);
        Page = await context.NewPageAsync();
        Page.Console += (_, m) =>
        {
            if (m.Type == "error" && !allowed.Exists(a => m.Text.Contains(a, StringComparison.Ordinal)))
            {
                errors.Add("console: " + m.Text);
            }
        };
        Page.PageError += (_, e) => errors.Add("pageerror: " + e);
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        Assert.True(errors.Count == 0, "Browser errors:\n" + string.Join("\n", errors));
    }

    /// <summary>Navigates and waits until the app has mounted (the nav is rendered by Wasm, not prerendered).</summary>
    protected async Task OpenAsync(string path)
    {
        await Page.GotoAsync(path);
        await Page.Locator("#app:not([data-nwui-prerender]) nav a").First.WaitForAsync();
    }

    protected ILocator Nav(string href) => Page.Locator($"nav a[href=\"{href}\"]");

    /// <summary>Ignores console errors containing <paramref name="text"/> (e.g. a deliberate 404).</summary>
    protected void AllowConsoleError(string text) => allowed.Add(text);
}
