using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Fetch;

public sealed class User
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Email { get; set; } = "";
}

public sealed class UserDetails
{
    public int Id { get; set; }

    public string Bio { get; set; } = "";
}

/// <summary>System.Text.Json source generation: NetWasm has no reflection-based serializer.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(User[]))]
[JsonSerializable(typeof(UserDetails))]
public sealed partial class ApiJson : JsonSerializerContext
{
}

/// <summary>A small typed client over HttpClient (which NetWasm implements with fetch).</summary>
public sealed class Api(HttpClient http)
{
    public async Task<User[]> GetUsersAsync(CancellationToken ct)
    {
        var json = await GetAsync("api/users.json", ct);
        return JsonSerializer.Deserialize(json, ApiJson.Default.UserArray) ?? [];
    }

    public async Task<UserDetails> GetDetailsAsync(int id, CancellationToken ct)
    {
        var json = await GetAsync($"api/users/{id}.json", ct);
        return JsonSerializer.Deserialize(json, ApiJson.Default.UserDetails)!;
    }

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
