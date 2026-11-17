using System.Net.WebSockets;
using System.Text;

namespace NetWasm.UI.DevServer;

/// <summary>The open reload-client WebSockets; broadcasts building/reload/error messages to them.</summary>
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
