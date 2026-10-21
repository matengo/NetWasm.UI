using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace NetWasm.UI.E2E;

/// <summary>
/// Publishes and serves <c>samples/Counter</c> with the dev server (once per test run) and launches Chromium.
/// Set <c>NETWASM_UI_E2E_URL</c> (e.g. http://localhost:8765) to test an already running server instead.
/// </summary>
public sealed class AppServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private readonly StringBuilder log = new();
    private Process? server;
    private IPlaywright? playwright;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("NETWASM_UI_E2E_URL");
        BaseUrl = string.IsNullOrEmpty(external) ? await StartServerAsync() : external.TrimEnd('/');

        playwright = await Playwright.CreateAsync();
        try
        {
            Browser = await playwright.Chromium.LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" });
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "Chromium for Playwright is not installed. Run: pwsh tests/NetWasm.UI.E2E/bin/Debug/net10.0/playwright.ps1 install chromium",
                ex);
        }
    }

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
        foreach (var a in new[] { "run", "--project", Path.Combine("tools", "DevServer"), "--", Path.Combine("samples", "Counter"), "--port", port.ToString(), "--no-watch" })
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

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppServerFixture>
{
    public const string Name = "app";
}

/// <summary>Base class: a fresh browser context per test, failing the test on any console error or page error.</summary>
[Collection(AppCollection.Name)]
public abstract class PageTest : IAsyncLifetime
{
    private readonly List<string> errors = new();
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
            if (m.Type == "error")
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

    /// <summary>Navigates and waits until the app has mounted (the nav is rendered by Wasm).</summary>
    protected async Task OpenAsync(string path)
    {
        await Page.GotoAsync(path);
        await Page.Locator("nav a").First.WaitForAsync();
    }

    protected ILocator Nav(string href) => Page.Locator($"nav a[href=\"{href}\"]");
}
