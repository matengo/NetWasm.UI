namespace NetWasm.UI.DevServer;

internal sealed class DevServerOptions
{
    public string ProjectPath { get; private set; } = string.Empty;
    public string WatchRoot { get; private set; } = string.Empty;
    public string Configuration { get; private set; } = "Debug";
    /// <summary>NetWasm optimization level. Debug + O0 recompiles only changed methods (NetWasm 0.11+).</summary>
    public string Optimization { get; private set; } = "O0";
    public int Port { get; private set; } = 8765;
    public bool NoWatch { get; private set; }
    public bool SkipInitialPublish { get; private set; }

    public static DevServerOptions Parse(string[] args)
    {
        var o = new DevServerOptions();
        string? project = null;
        string? watch = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p":
                    project = args[++i];
                    break;
                case "--watch-root" or "-w":
                    watch = args[++i];
                    break;
                case "--port":
                    o.Port = int.Parse(args[++i]);
                    break;
                case "--configuration" or "-c":
                    o.Configuration = args[++i];
                    break;
                case "--optimization" or "-O":
                    o.Optimization = args[++i];
                    break;
                case "--release":
                    o.Configuration = "Release";
                    o.Optimization = "Oz";
                    break;
                case "--no-watch":
                    o.NoWatch = true;
                    break;
                case "--no-publish":
                    o.SkipInitialPublish = true;
                    break;
                case "--help" or "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    project ??= args[i];
                    break;
            }
        }

        o.ProjectPath = ResolveProject(project ?? Directory.GetCurrentDirectory());
        o.WatchRoot = Path.GetFullPath(watch ?? FindRepoRoot(Path.GetDirectoryName(o.ProjectPath)!));
        return o;
    }

    private static string ResolveProject(string pathOrDir)
    {
        var full = Path.GetFullPath(pathOrDir);
        if (File.Exists(full))
        {
            return full;
        }

        var candidates = Directory.GetFiles(full, "*.csproj");
        return candidates.Length == 1
            ? candidates[0]
            : throw new ArgumentException($"Expected exactly one .csproj in '{full}', found {candidates.Length}. Pass --project.");
    }

    private static string FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        return start;
    }

    private static void PrintHelp() => Console.WriteLine(
        """
        NetWasm.UI dev server – publishes a NetWasm app, serves it with Brotli, and reloads the browser on change.

        usage: dotnet nwui-dev [project-or-dir] [options]
           or: dotnet run --project tools/DevServer -- [project-or-dir] [options]

          -p, --project <csproj|dir>   App project (default: current directory)
          -w, --watch-root <dir>       Directory to watch (default: repo root)
          -c, --configuration <cfg>    Build configuration (default: Debug)
          -O, --optimization <lvl>     NetWasmOptimization: O0 | O1 | Oz (default: O0 – incremental, fastest rebuilds)
              --release                Shorthand for -c Release -O Oz (production sizes)
              --port <n>               Port (default: 8765)
              --no-watch               Serve only, do not rebuild on change
              --no-publish             Skip the initial publish
        """);
}
