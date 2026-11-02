using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

// Builds the NetWasm.UI, SolidJS, React and Blazor benchmark apps, serves them with Kestrel and measures them in Chromium.
// Usage: dotnet run --project benchmarks/Runner -c Release -- [--runs N] [--skip-build] [--no-o3] [--no-aot] [--headed]
// --no-aot skips Blazor AOT, which needs the wasm-tools workload and takes minutes to build.

var runs = 10;
var skipBuild = false;
var headed = false;
var withO3 = true;
var withAot = true;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--skip-build": skipBuild = true; break;
        case "--headed": headed = true; break;
        case "--no-o3": withO3 = false; break;
        case "--no-aot": withAot = false; break;
        default: Console.Error.WriteLine("Unknown option: " + args[i]); return 1;
    }
}

var root = FindRepoRoot();
var bench = Path.Combine(root, "benchmarks");
var outDir = Path.Combine(bench, ".out");
var apps = new List<(string Name, string Path, string Dir)>
{
    ("NetWasm.UI (Oz)", "netwasm-oz", Path.Combine(outDir, "netwasm-oz")),
};
if (withO3)
{
    apps.Add(("NetWasm.UI (O3)", "netwasm-o3", Path.Combine(outDir, "netwasm-o3")));
}

apps.Add(("SolidJS", "solid", Path.Combine(bench, "solid", "dist")));
apps.Add(("React", "react", Path.Combine(bench, "react", "dist")));
apps.Add(("Blazor", "blazor", Path.Combine(outDir, "blazor", "wwwroot")));
if (withAot)
{
    apps.Add(("Blazor (AOT)", "blazor-aot", Path.Combine(outDir, "blazor-aot", "wwwroot")));
}

var solidIndex = apps.FindIndex(a => a.Path == "solid");

if (!skipBuild)
{
    var project = Path.Combine(bench, "NetWasmBench", "NetWasmBench.csproj");
    Run("dotnet", root, "publish", project, "-c", "Release", "-o", apps[0].Dir, "-p:NetWasmOptimization=Oz", "--nologo", "-v", "q");
    if (withO3)
    {
        Run("dotnet", root, "publish", project, "-c", "Release", "-o", apps[1].Dir, "-p:NetWasmOptimization=O3", "--nologo", "-v", "q");
    }

    foreach (var js in new[] { "solid", "react" })
    {
        var dir = Path.Combine(bench, js);
        if (!Directory.Exists(Path.Combine(dir, "node_modules")))
        {
            Npm(dir, "ci");
        }

        Npm(dir, "run", "build");
    }

    var blazor = Path.Combine(bench, "BlazorBench", "BlazorBench.csproj");
    foreach (var stale in new[] { "blazor", "blazor-aot" })
    {
        // publish -o never deletes files, and fingerprinted names would pile up.
        if (Directory.Exists(Path.Combine(outDir, stale)))
        {
            Directory.Delete(Path.Combine(outDir, stale), recursive: true);
        }
    }

    Run("dotnet", root, "publish", blazor, "-c", "Release", "-o", Path.Combine(outDir, "blazor"), "--nologo", "-v", "q");
    if (withAot)
    {
        // A separate obj directory, so switching AOT on and off never reuses the other build's native output.
        Run("dotnet", root, "publish", blazor, "-c", "Release", "-o", Path.Combine(outDir, "blazor-aot"),
            "-p:RunAOTCompilation=true", @"-p:IntermediateOutputPath=obj\ReleaseAot\", "--nologo", "-v", "q");
    }
}

foreach (var app in apps)
{
    if (!File.Exists(Path.Combine(app.Dir, "index.html")))
    {
        Console.Error.WriteLine($"Missing {app.Dir}{Path.DirectorySeparatorChar}index.html. Run without --skip-build.");
        return 1;
    }
}

var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
var web = builder.Build();
var types = new FileExtensionContentTypeProvider();
types.Mappings[".mjs"] = "text/javascript";
types.Mappings[".wasm"] = "application/wasm";
foreach (var app in apps)
{
    var files = new PhysicalFileProvider(app.Dir);
    web.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RequestPath = "/" + app.Path });
    web.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        RequestPath = "/" + app.Path,
        ContentTypeProvider = types,
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream",
    });
}

await web.StartAsync();
var baseUrl = web.Urls.First();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Headless = !headed,
    Args = ["--js-flags=--expose-gc"],
});

Console.WriteLine($"Chromium {browser.Version}, {runs} runs per case, median shown.\n");

// Each DOM case: setup clicks, the measured click, and a check that the DOM ended up as expected.
// Every check is false before the click, so frameworks that render after the event (React, Blazor) are
// timed until their DOM is done, not until the click returns.
var ops = new (string Name, string[] Prep, string Click, string Check)[]
{
    ("create 1,000 rows", [.. Repeat(5, "#run", "#clear")], "#run", "rows() === 1000"),
    ("replace 1,000 rows", [.. Repeat(5, "#run")], "#run", "rows() === 1000 && id(1) !== before1"),
    ("partial update (every 10th)", ["#run", .. Repeat(5, "#update")], "#update",
        "label(1).endsWith(' !!!'.repeat(6)) && !label(2).endsWith('!')"),
    ("select row", ["#run"], "tbody tr:nth-child(2) td:nth-child(2) a",
        "document.querySelectorAll('tr.danger').length === 1 && document.querySelector('tbody tr:nth-child(2)').className === 'danger'"),
    ("swap rows", ["#run", .. Repeat(5, "#swaprows")], "#swaprows", "rows() === 1000 && id(2) !== before2"),
    ("remove row", ["#run", .. Repeat(5, "tbody tr:nth-child(4) td:nth-child(3) a")], "tbody tr:nth-child(4) td:nth-child(3) a",
        "rows() === 994"),
    ("create 10,000 rows", [], "#runlots", "rows() === 10000"),
    ("append 1,000 rows", ["#run"], "#add", "rows() === 2000"),
    ("clear 1,000 rows", [.. Repeat(5, "#run", "#clear"), "#run"], "#clear", "rows() === 0"),
};

const string Helpers = """
    const rows = () => document.querySelectorAll('tbody tr').length;
    const label = n => document.querySelector(`tbody tr:nth-child(${n}) td:nth-child(2) a`).textContent;
    const id = n => +document.querySelector(`tbody tr:nth-child(${n}) td:nth-child(1)`).textContent;
    """;

var table = new List<(string Case, string Unit, double[] Values)>();

foreach (var (name, prep, click, check) in ops)
{
    var values = new double[apps.Count];
    var layouts = new double[apps.Count];
    for (var a = 0; a < apps.Count; a++)
    {
        var script = new List<double>();
        var total = new List<double>();
        for (var r = 0; r < runs; r++)
        {
            await using var context = await browser.NewContextAsync();
            var page = await OpenAsync(context, apps[a].Path);
            foreach (var p in prep)
            {
                // Prep clicks may render asynchronously too; let the event loop settle after each.
                await page.EvaluateAsync("s => { document.querySelector(s).click(); return new Promise(r => setTimeout(r, 0)); }", p);
            }

            var result = await page.EvaluateAsync<double[]>($$"""
                async sel => {
                  {{Helpers}}
                  const before1 = rows() >= 1 ? id(1) : 0;
                  const before2 = rows() >= 2 ? id(2) : 0;
                  const el = document.querySelector(sel);
                  window.gc();
                  await new Promise(r => setTimeout(r, 20));
                  const t0 = performance.now();
                  el.click();
                  // Synchronous renderers pass at once. Others get microtasks first, then macrotasks.
                  for (let n = 0; !({{check}}); n++) {
                    if (n > 2000) throw new Error('Check failed: {{check.Replace("'", "\\'")}}');
                    await (n < 100 ? Promise.resolve() : new Promise(r => setTimeout(r, 0)));
                  }
                  const t1 = performance.now();
                  document.body.offsetHeight;
                  const t2 = performance.now();
                  return [t1 - t0, t2 - t0];
                }
                """, click);
            script.Add(result[0]);
            total.Add(result[1]);
        }

        values[a] = Median(script);
        layouts[a] = Median(total);
        Console.Write('.');
    }

    table.Add((name, "script", values));
    table.Add((name, "script + layout", layouts));
}

Console.WriteLine();

// DOM-free workloads, run back to back in one page per app after a warm-up.
var workloads = new[] { "deep", "broad", "diamond", "create", "sort", "sieve" };
var startup = new double[apps.Count];
var reactive = new double[workloads.Length, apps.Count];
for (var a = 0; a < apps.Count; a++)
{
    var ready = new List<double>();
    for (var r = 0; r < runs; r++)
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context, apps[a].Path);
        ready.Add(await page.EvaluateAsync<double>("window.__ready"));
    }

    startup[a] = Median(ready);

    await using var ctx = await browser.NewContextAsync();
    var p = await OpenAsync(ctx, apps[a].Path);
    for (var w = 0; w < workloads.Length; w++)
    {
        var times = new List<double>();
        int? checksum = null;
        for (var r = 0; r < runs + 3; r++)
        {
            var result = await p.EvaluateAsync<double[]?>("""
                name => {
                  window.gc();
                  const t0 = performance.now();
                  const c = window.bench.reactive(name);
                  return c === null ? null : [performance.now() - t0, c];
                }
                """, workloads[w]);
            if (result is null)
            {
                // Not applicable to this framework (e.g. no signals in React or Blazor).
                times.Add(double.NaN);
                break;
            }

            checksum ??= (int)result[1];
            if (checksum != (int)result[1])
            {
                throw new InvalidOperationException($"{apps[a].Name} {workloads[w]}: unstable checksum");
            }

            if (r >= 3)
            {
                times.Add(result[0]);
            }
        }

        reactive[w, a] = Median(times);
        if (checksum is int c)
        {
            Checksums.Record(workloads[w], apps[a].Name, c);
        }
    }

    Console.Write('.');
}

// What a first visit downloads: every response body, raw and Brotli-compressed (quality 11, as in production).
var rawKb = new double[apps.Count];
var brotliKb = new double[apps.Count];
for (var a = 0; a < apps.Count; a++)
{
    (rawKb[a], brotliKb[a]) = await MeasureDownloadAsync(apps[a].Path);
}

Console.WriteLine("\n");
Checksums.Verify();

var md = new StringBuilder();
var header = "| Case | " + string.Join(" | ", apps.Select(a => a.Name)) + " | NetWasm.UI (Oz) / SolidJS |";
md.AppendLine(header);
md.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", apps.Count + 2)));
foreach (var (name, unit, values) in table)
{
    AppendRow(name + " — " + unit, values);
}

for (var w = 0; w < workloads.Length; w++)
{
    AppendRow("reactive: " + workloads[w], Enumerable.Range(0, apps.Count).Select(a => reactive[w, a]).ToArray());
}

AppendRow("startup (`__ready`)", startup);
AppendRow("download, raw (KB)", rawKb);
AppendRow("download, Brotli (KB)", brotliKb);

Console.WriteLine("All times in ms (median). – means not applicable.\n");
Console.WriteLine(md);
await web.StopAsync();
return 0;

void AppendRow(string label, double[] values) =>
    md.AppendLine($"| {label} | {string.Join(" | ", values.Select(Format))} | " +
        $"{(values[0] / values[solidIndex]).ToString("0.00", CultureInfo.InvariantCulture)}× |");

static string Format(double v) => double.IsNaN(v) ? "–" : v.ToString("0.0", CultureInfo.InvariantCulture);

async Task<(double Raw, double Brotli)> MeasureDownloadAsync(string path)
{
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var bodies = new List<Task<byte[]>>();
    page.Response += (_, response) =>
    {
        if (response.Status == 200)
        {
            bodies.Add(response.BodyAsync());
        }
    };
    await page.GotoAsync($"{baseUrl}/{path}/");
    await page.WaitForFunctionAsync("window.__ready !== undefined");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    long raw = 0, compressed = 0;
    foreach (var body in await Task.WhenAll(bodies))
    {
        raw += body.Length;
        using var ms = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(body);
        }

        compressed += ms.Length;
    }

    return (raw / 1024.0, compressed / 1024.0);
}

async Task<IPage> OpenAsync(IBrowserContext context, string path)
{
    var page = await context.NewPageAsync();
    page.PageError += (_, e) => Console.Error.WriteLine($"[{path}] page error: {e}");
    await page.GotoAsync($"{baseUrl}/{path}/");
    await page.WaitForFunctionAsync("window.__ready !== undefined");
    return page;
}

static IEnumerable<string> Repeat(int count, params string[] selectors) =>
    Enumerable.Repeat(selectors, count).SelectMany(s => s);

static double Median(List<double> values)
{
    values.Sort();
    var n = values.Count;
    return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
}

static void Npm(string dir, params string[] args) =>
    Run(OperatingSystem.IsWindows() ? "cmd" : "npm", dir, OperatingSystem.IsWindows() ? ["/c", "npm", .. args] : args);

static void Run(string file, string dir, params string[] args)
{
    Console.WriteLine($"> {file} {string.Join(' ', args)}");
    var psi = new ProcessStartInfo(file) { WorkingDirectory = dir, UseShellExecute = false };
    foreach (var a in args)
    {
        psi.ArgumentList.Add(a);
    }

    using var process = Process.Start(psi)!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{file} exited with {process.ExitCode}");
    }
}

static string FindRepoRoot()
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

/// <summary>Both implementations of a workload must compute the same checksum, or the comparison is meaningless.</summary>
internal static class Checksums
{
    private static readonly Dictionary<string, List<(string App, int Value)>> Values = new();

    public static void Record(string workload, string app, int value)
    {
        if (!Values.TryGetValue(workload, out var list))
        {
            Values[workload] = list = new();
        }

        list.Add((app, value));
    }

    public static void Verify()
    {
        foreach (var (workload, list) in Values)
        {
            if (list.Select(v => v.Value).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for {workload}: " + string.Join(", ", list.Select(v => $"{v.App}={v.Value}")));
            }
        }
    }
}
