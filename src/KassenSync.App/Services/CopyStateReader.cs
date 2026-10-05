using System.Text.Json;
using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.App.Services;

public sealed class CopyStateReader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<CopyOperationState> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CopyStateStore.StatePath)) return CopyOperationState.Idle;
        try
        {
            await using var stream = new FileStream(CopyStateStore.StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<CopyOperationState>(stream, JsonOptions, cancellationToken)
                   ?? CopyOperationState.Idle;
        }
        catch (IOException)
        {
            return CopyOperationState.Idle;
        }
        catch (JsonException)
        {
            return CopyOperationState.Idle;
        }
    }
}
