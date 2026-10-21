using System.Diagnostics;
using System.Text;

namespace NetWasm.UI.DevServer;

internal sealed record PublishResult(bool Success, string Output, TimeSpan Elapsed);

internal sealed class Publisher(DevServerOptions options)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> ResolvePublishDirAsync()
    {
        var (code, output) = await RunDotnetAsync(
            $"msbuild \"{options.ProjectPath}\" -nologo -getProperty:PublishDir -p:Configuration={options.Configuration}");
        if (code != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Could not resolve PublishDir:\n" + output);
        }

        var dir = output.Trim();
        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(Path.GetDirectoryName(options.ProjectPath)!, dir));
    }

    public async Task<PublishResult> PublishAsync()
    {
        await gate.WaitAsync();
        try
        {
            Console.Write($"[{DateTime.Now:HH:mm:ss}] publishing… ");
            var sw = Stopwatch.StartNew();
            var (code, output) = await RunDotnetAsync(
                $"publish \"{options.ProjectPath}\" -c {options.Configuration} -nologo -v q -clp:NoSummary");
            sw.Stop();
            var ok = code == 0;
            Console.WriteLine(ok ? $"ok ({sw.Elapsed.TotalSeconds:0.0}s)" : $"FAILED ({sw.Elapsed.TotalSeconds:0.0}s)");
            return new PublishResult(ok, output, sw.Elapsed);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<(int Code, string Output)> RunDotnetAsync(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var sb = new StringBuilder(await stdout);
        var err = await stderr;
        if (err.Length > 0)
        {
            sb.AppendLine().Append(err);
        }

        return (process.ExitCode, sb.ToString());
    }
}
