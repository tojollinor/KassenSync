using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using KassenSync.Core.Ipc;

namespace KassenSync.App.Services;

public sealed class IpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T> SendAsync<T>(string type, object? payload = null, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        await using var pipe = new NamedPipeClientStream(
            ".",
            IpcConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutCts.Token);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

        var request = new IpcRequest
        {
            Type = type,
            Payload = JsonSerializer.SerializeToElement(payload)
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));

        var line = await reader.ReadLineAsync(timeoutCts.Token);
        if (string.IsNullOrWhiteSpace(line))
            throw new IOException("Der KassenSync-Dienst hat keine Antwort geliefert.");

        var response = JsonSerializer.Deserialize<IpcResponse>(line, JsonOptions)
                       ?? throw new IOException("Ungültige Antwort des KassenSync-Dienstes.");
        if (!response.Success)
            throw new InvalidOperationException(response.Error ?? "Unbekannter Dienstfehler.");

        if (typeof(T) == typeof(object))
            return (T)(object)new object();

        return response.Payload.Deserialize<T>(JsonOptions)
               ?? throw new InvalidOperationException("Die Antwort konnte nicht gelesen werden.");
    }
}
