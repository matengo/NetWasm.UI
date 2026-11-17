using System;

namespace NetWasmApp.Shared;

/// <summary>Returned by the server's API and deserialized by the client.</summary>
public sealed class WeatherForecast
{
    // DateTime rather than DateOnly: NetWasm's System.Text.Json has no DateOnly converter.
    public DateTime Date { get; set; }

    public int TemperatureC { get; set; }

    public string? Summary { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
