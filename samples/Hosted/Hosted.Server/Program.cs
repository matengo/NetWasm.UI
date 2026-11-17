using Hosted.Shared;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/javascript", "application/wasm"]);
});

var app = builder.Build();

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    // Live reload: a change in the client rebuilds it and reloads the browser, keeping signal and form state.
    app.UseNetWasmUIDevServer();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/api/weatherforecast", () => Enumerable.Range(1, 5).Select(i => new WeatherForecast
{
    Date = DateTime.UtcNow.Date.AddDays(i),
    TemperatureC = Random.Shared.Next(-20, 55),
    Summary = summaries[Random.Shared.Next(summaries.Length)],
}).ToArray());

// Unknown API paths are 404s, not client routes.
app.MapFallback("/api/{**path}", () => Results.NotFound());

// The NetWasm.UI client (NetWasmUIClientProject in Hosted.Server.csproj): its files, and a page for every other path.
// The client prerenders its routes (see Hosted.Client.csproj), so /counter gets counter.html and shows content before
// the Wasm has loaded; other routes get the bare shell.html and render in the browser.
app.MapNetWasmUI();

app.Run();