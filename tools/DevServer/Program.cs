using System.IO.Compression;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NetWasm.UI.DevServer;

var options = DevServerOptions.Parse(args);
var publisher = new Publisher(options);
var reload = new ReloadHub();

Console.WriteLine($"Project:   {options.ProjectPath}");
Console.WriteLine($"Watching:  {options.WatchRoot}");
Console.WriteLine($"Build:     -c {options.Configuration} -O {options.Optimization}{(options.Optimization != "Oz" ? "  (use --release for production sizes)" : string.Empty)}");

var publishDir = await publisher.ResolvePublishDirAsync();
Console.WriteLine($"Publish:   {publishDir}");

if (!options.SkipInitialPublish)
{
    var initial = await publisher.PublishAsync();
    if (!initial.Success)
    {
        Console.Error.WriteLine(initial.Output);
    }
}

Directory.CreateDirectory(publishDir);

var builder = WebApplication.CreateBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://localhost:{options.Port}");
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/wasm", "text/javascript"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);

var app = builder.Build();
app.UseResponseCompression();
app.UseWebSockets();

var reloadScript = ReadEmbedded("reload-client.js");
app.MapGet("/__netwasm_ui/reload-client.js", () => Results.Text(reloadScript, "text/javascript"));
app.Map("/__netwasm_ui/reload", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await reload.HoldAsync(socket, context.RequestAborted);
});

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".mjs"] = "text/javascript";
contentTypes.Mappings[".mts"] = "text/typescript";
contentTypes.Mappings[".wasm"] = "application/wasm";

// Serve HTML ourselves so the reload client can be injected.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    if (path.EndsWith('/'))
    {
        path += "index.html";
    }

    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
        var file = Path.Combine(publishDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file))
        {
            var html = await File.ReadAllTextAsync(file);
            html = InjectScript(html, "/__netwasm_ui/reload-client.js");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(html);
            return;
        }
    }

    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(publishDir),
    ContentTypeProvider = contentTypes,
    ServeUnknownFileTypes = true,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-store",
});

if (!options.NoWatch)
{
    var watcher = new SourceWatcher(options.WatchRoot, async changed =>
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] change: {string.Join(", ", changed.Select(c => Path.GetRelativePath(options.WatchRoot, c)))}");
        await reload.BroadcastAsync("""{"type":"building"}""");
        var result = await publisher.PublishAsync();
        if (result.Success)
        {
            await reload.BroadcastAsync("""{"type":"reload"}""");
        }
        else
        {
            Console.Error.WriteLine(result.Output);
            await reload.BroadcastAsync(ReloadHub.ErrorMessage(result.Output));
        }
    });
    app.Lifetime.ApplicationStopping.Register(watcher.Dispose);
}

Console.WriteLine($"Listening: http://localhost:{options.Port}  (Brotli/gzip on, {(options.NoWatch ? "watch off" : "watch on")})");
await app.RunAsync();

static string ReadEmbedded(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource '{name}' missing");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
}

static string InjectScript(string html, string src)
{
    var tag = $"<script src=\"{src}\"></script>";
    var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
    return idx >= 0 ? html.Insert(idx, tag + "\n") : html + tag;
}

internal sealed class ReloadHub
{
    private readonly List<WebSocket> sockets = [];
    private readonly Lock gate = new();

    public async Task HoldAsync(WebSocket socket, CancellationToken ct)
    {
        lock (gate)
        {
            sockets.Add(socket);
        }

        var buffer = new byte[64];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await socket.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            lock (gate)
            {
                sockets.Remove(socket);
            }
        }
    }

    public async Task BroadcastAsync(string json)
    {
        WebSocket[] targets;
        lock (gate)
        {
            targets = [.. sockets];
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var s in targets)
        {
            try
            {
                await s.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }
    }

    public static string ErrorMessage(string text) =>
        $$"""{"type":"error","text":{{System.Text.Json.JsonSerializer.Serialize(text)}}}""";
}
