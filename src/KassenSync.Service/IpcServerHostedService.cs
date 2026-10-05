using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KassenSync.Core.Ipc;
using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class IpcServerHostedService(
    IndexDatabase database,
    SettingsStore settingsStore,
    ILogger<IpcServerHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IPC server error.");
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line)) return;

        IpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(line, JsonOptions)
                          ?? throw new InvalidOperationException("Ungültige IPC-Anfrage.");
            response = await HandleRequestAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IPC request failed.");
            response = IpcResponse.Fail(ex.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private async Task<IpcResponse> HandleRequestAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Type)
        {
            case IpcMessageTypes.GetStatus:
                return IpcResponse.Ok(new ServiceStatus(true, DateTime.UtcNow));
            case IpcMessageTypes.GetFiles:
                return IpcResponse.Ok(await database.GetAllAsync(cancellationToken));
            case IpcMessageTypes.GetSettings:
                return IpcResponse.Ok(await settingsStore.LoadAsync(cancellationToken));
            case IpcMessageTypes.SaveSettings:
            {
                var settings = request.Payload.Deserialize<AppSettings>(JsonOptions)
                               ?? throw new InvalidOperationException("Einstellungen konnten nicht gelesen werden.");
                AppSettingsValidator.Validate(settings);
                await settingsStore.SaveAsync(settings, cancellationToken);
                return IpcResponse.Ok(settings);
            }
            case IpcMessageTypes.RecopyFiles:
            {
                var recopy = request.Payload.Deserialize<RecopyRequest>(JsonOptions)
                             ?? throw new InvalidOperationException("Dateiauswahl konnte nicht gelesen werden.");
                await database.QueueForRecopyAsync(recopy.FileIds, cancellationToken);
                return IpcResponse.Ok(new { queued = recopy.FileIds.Distinct().Count() });
            }
            case IpcMessageTypes.DeleteFiles:
            {
                var delete = request.Payload.Deserialize<DeleteFilesRequest>(JsonOptions)
                             ?? throw new InvalidOperationException("Dateiauswahl konnte nicht gelesen werden.");
                var deleted = await database.DeleteByIdsAsync(delete.FileIds, cancellationToken);
                return IpcResponse.Ok(new { deleted });
            }
            default:
                return IpcResponse.Fail($"Unbekannter IPC-Befehl: {request.Type}");
        }
    }
}
