using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

// Builds the NetWasm.UI and SolidJS benchmark apps, serves them with Kestrel and measures them in Chromium.
// Usage: dotnet run --project benchmarks/Runner -c Release -- [--runs N] [--skip-build] [--no-o3] [--headed]

var runs = 10;
var skipBuild = false;
var headed = false;
var withO3 = true;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--skip-build": skipBuild = true; break;
        case "--headed": headed = true; break;
        case "--no-o3": withO3 = false; break;
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

if (!skipBuild)
{
    var project = Path.Combine(bench, "NetWasmBench", "NetWasmBench.csproj");
    Run("dotnet", root, "publish", project, "-c", "Release", "-o", apps[0].Dir, "-p:NetWasmOptimization=Oz", "--nologo", "-v", "q");
    if (withO3)
    {
        Run("dotnet", root, "publish", project, "-c", "Release", "-o", apps[1].Dir, "-p:NetWasmOptimization=O3", "--nologo", "-v", "q");
    }

    var solid = Path.Combine(bench, "solid");
    if (!Directory.Exists(Path.Combine(solid, "node_modules")))
    {
        Npm(solid, "ci");
    }

    Npm(solid, "run", "build");
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
    web.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = "/" + app.Path, ContentTypeProvider = types });
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
var ops = new (string Name, string[] Prep, string Click, string Check)[]
{
    ("create 1,000 rows", [.. Repeat(5, "#run", "#clear")], "#run", "rows() === 1000"),
    ("replace 1,000 rows", [.. Repeat(5, "#run")], "#run", "rows() === 1000"),
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
                await page.EvaluateAsync("s => document.querySelector(s).click()", p);
            }

            var result = await page.EvaluateAsync<double[]>($$"""
                async sel => {
                  {{Helpers}}
                  const before2 = rows() >= 2 ? id(2) : 0;
                  const el = document.querySelector(sel);
                  window.gc();
                  await new Promise(r => setTimeout(r, 20));
                  const t0 = performance.now();
                  el.click();
                  const t1 = performance.now();
                  document.body.offsetHeight;
                  const t2 = performance.now();
                  if (!({{check}})) throw new Error('Check failed: {{check.Replace("'", "\\'")}}');
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
            var result = await p.EvaluateAsync<double[]>("""
                name => {
                  window.gc();
                  const t0 = performance.now();
                  const c = window.bench.reactive(name);
                  return [performance.now() - t0, c];
                }
                """, workloads[w]);
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
        Checksums.Record(workloads[w], apps[a].Name, checksum!.Value);
    }

    Console.Write('.');
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

Console.WriteLine("All times in ms (median).\n");
Console.WriteLine(md);
await web.StopAsync();
return 0;

void AppendRow(string label, double[] values) =>
    md.AppendLine($"| {label} | {string.Join(" | ", values.Select(v => v.ToString("0.0", CultureInfo.InvariantCulture)))} | " +
        $"{(values[0] / values[^1]).ToString("0.00", CultureInfo.InvariantCulture)}× |");

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
