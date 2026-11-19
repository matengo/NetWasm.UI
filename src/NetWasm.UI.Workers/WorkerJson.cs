using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NetWasm.UI.Workers;

/// <summary>Sends non-native values between the app and workers as JSON. Used by generated code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class WorkerJson
{
    /// <summary>The metadata for <typeparamref name="T"/> in <paramref name="context"/>.</summary>
    public static JsonTypeInfo<T> Info<T>(JsonSerializerContext context) =>
        context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException("The worker contract's JsonContext does not list " + typeof(T).Name + "; add [JsonSerializable(typeof(" + typeof(T).Name + "))].");

    /// <summary>Serializes <paramref name="value"/>.</summary>
    public static string Serialize<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    /// <summary>Deserializes <paramref name="json"/>.</summary>
    public static T Deserialize<T>(string? json, JsonTypeInfo<T> info) => JsonSerializer.Deserialize(json ?? "null", info)!;
}
