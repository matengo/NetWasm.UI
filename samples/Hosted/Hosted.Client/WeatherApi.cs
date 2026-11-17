using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Hosted.Shared;

namespace Hosted.Client;

/// <summary>System.Text.Json source generation: NetWasm has no reflection-based serializer.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WeatherForecast[]))]
public sealed partial class ApiJson : JsonSerializerContext
{
}

public sealed class WeatherApi(HttpClient http)
{
    public async Task<WeatherForecast[]> GetForecastAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/weatherforecast", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, ApiJson.Default.WeatherForecastArray) ?? [];
    }
}
