using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NetWasm.UI.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Hosts a NetWasm.UI app (the project in <c>NetWasmUIClientProject</c>) in an ASP.NET Core app.</summary>
public static class NetWasmUIWebApplicationExtensions
{
    /// <summary>
    /// Serves the NetWasm.UI client: its files, and a page for every other path without a file extension (client-side
    /// routes). <c>/counter</c> gets <c>counter.html</c> when the client prerendered that route, otherwise
    /// <c>shell.html</c>, <c>404.html</c> or <c>index.html</c>, the first that exists. Call it after the app's own
    /// endpoints; they take precedence.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="clientPath">
    /// The client's folder, relative to the app's base directory; <c>NetWasmUIClientFolder</c> in the project file.
    /// </param>
    /// <returns>The fallback endpoint that serves the pages.</returns>
    public static IEndpointConventionBuilder MapNetWasmUI(this WebApplication app, string clientPath = "client")
    {
        ArgumentNullException.ThrowIfNull(app);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, clientPath));
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"The NetWasm.UI client was not found in '{root}'. Set NetWasmUIClientProject in the project file to the client's .csproj.");
        }

        var files = new PhysicalFileProvider(root);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".mjs"] = "text/javascript";
        types.Mappings[".wasm"] = "application/wasm";
        // Debug symbol sidecars: readable type names and stack frames in NetWasm's error messages.
        types.Mappings[".type-symbols"] = "application/octet-stream";
        types.Mappings[".method-symbols"] = "application/octet-stream";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = types,
            // The app's files keep their names between builds: revalidate (cheap 304s) instead of caching stale code.
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });

        return app.MapFallback("{*path:nonfile}", async context =>
        {
            var route = context.Request.Path.Value!.Trim('/');
            var page = route.Length == 0 ? files.GetFileInfo("index.html") : files.GetFileInfo(route + ".html");
            foreach (var name in new[] { "shell.html", "404.html", "index.html" })
            {
                if (page.Exists)
                {
                    break;
                }

                page = files.GetFileInfo(name);
            }

            if (!page.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var html = await File.ReadAllTextAsync(page.PhysicalPath!, context.RequestAborted);
            if (((IApplicationBuilder)app).Properties.TryGetValue(ClientDevServer.PropertyKey, out var dev) && dev is ClientDevServer)
            {
                html = ClientDevServer.InjectReloadClient(html);
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync(html, context.RequestAborted);
        });
    }

    /// <summary>
    /// Live reload for the NetWasm.UI client, for development: republishes the client when its files (or those of the
    /// projects it references) change and reloads the browser, keeping <c>int</c>/<c>bool</c>/<c>string</c> signal
    /// values, form fields, focus and scroll position. Build errors are shown in the page. Call it before
    /// <see cref="MapNetWasmUI"/>, typically inside <c>if (app.Environment.IsDevelopment())</c>.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static WebApplication UseNetWasmUIDevServer(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NetWasm.UI.DevServer");
        var settings = ClientDevSettings.Load(AppContext.BaseDirectory);
        if (settings is null)
        {
            logger.LogWarning("NetWasm.UI live reload is off: no client was built into this app (set NetWasmUIClientProject in the project file).");
            return app;
        }

        var server = new ClientDevServer(settings, logger);
        ((IApplicationBuilder)app).Properties[ClientDevServer.PropertyKey] = server;
        app.UseWebSockets();
        app.MapGet(ClientDevServer.ScriptPath, () => Results.Text(ClientDevServer.Script, "text/javascript"));
        app.Map(ClientDevServer.SocketPath, server.AcceptAsync);
        app.Lifetime.ApplicationStarted.Register(server.Start);
        app.Lifetime.ApplicationStopping.Register(server.Dispose);
        return app;
    }
}
