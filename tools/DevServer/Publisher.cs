using System.Diagnostics;
using System.Text;

namespace NetWasm.UI.DevServer;

internal sealed record PublishResult(bool Success, string Output, TimeSpan Elapsed);

internal sealed class Publisher(DevServerOptions options)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool restored;

    public async Task<string> ResolvePublishDirAsync()
    {
        var (code, output) = await RunDotnetAsync(
            $"msbuild \"{options.ProjectPath}\" -nologo -getProperty:PublishDir -p:Configuration={options.Configuration}");
        if (code != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Could not resolve PublishDir:\n" + output);
        }

        var dir = output.Trim();
        if (Path.DirectorySeparatorChar == '/')
        {
            dir = dir.Replace('\\', '/');
        }

        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(Path.GetDirectoryName(options.ProjectPath)!, dir));
    }

    public async Task<PublishResult> PublishAsync()
    {
        await gate.WaitAsync();
        try
        {
            Console.Write($"[{DateTime.Now:HH:mm:ss}] publishing… ");
            var sw = Stopwatch.StartNew();
            var noRestore = restored ? " --no-restore" : string.Empty;
            var (code, output) = await RunDotnetAsync(
                $"publish \"{options.ProjectPath}\" -c {options.Configuration} -p:NetWasmOptimization={options.Optimization}{noRestore} -nologo -v q -clp:NoSummary");
            sw.Stop();
            var ok = code == 0;
            restored |= ok;
            Console.WriteLine(ok ? $"ok ({sw.Elapsed.TotalSeconds:0.0}s)" : $"FAILED ({sw.Elapsed.TotalSeconds:0.0}s)");
            return new PublishResult(ok, output, sw.Elapsed);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(int Code, string Output)> RunDotnetAsync(string arguments)
    {
        // Watch mode keeps MSBuild nodes and the compiler server warm between rebuilds. Without
        // watch (CI, E2E) they are off: they would inherit the output pipes and outlive the server.
        var reuse = !options.NoWatch;
        var psi = new ProcessStartInfo("dotnet", arguments + (reuse ? string.Empty : " -nodeReuse:false"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!reuse)
        {
            psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            psi.Environment["UseSharedCompilation"] = "false";
        }

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

        // Wait for the process itself, not for EOF on its pipes, which a leftover child may hold.
        await exited.Task;
        await Task.Delay(100);
        lock (sb)
        {
            return (process.ExitCode, sb.ToString());
        }
    }
}
