using System.Text.Json;
using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class CopyStateStore
{
    private readonly object _gate = new();
    private CopyOperationState _state = CopyOperationState.Idle;
    private bool _started;

    public static string StatePath => Path.Combine(AppPaths.DataDirectory, "copy-state.json");

    public string Begin(int totalFiles)
    {
        lock (_gate)
        {
            var id = Guid.NewGuid().ToString("N");
            _started = false;
            _state = new CopyOperationState(id, false, false, false, 0, totalFiles, string.Empty, 0,
                "Kopiervorgang wird vorbereitet", null, DateTime.UtcNow);
            Persist();
            return id;
        }
    }

    public void StartFile(string operationId, int currentFile, int totalFiles, string fileName)
    {
        lock (_gate)
        {
            if (!string.Equals(_state.OperationId, operationId, StringComparison.Ordinal)) return;
            _started = true;
            _state = new CopyOperationState(operationId, true, false, false, currentFile, totalFiles, fileName, 0,
                $"Kopiere {currentFile} von {totalFiles}: {fileName}", null, DateTime.UtcNow);
            Persist();
        }
    }

    public void Report(string operationId, CopyProgress progress)
    {
        lock (_gate)
        {
            if (!_started || !string.Equals(_state.OperationId, operationId, StringComparison.Ordinal)) return;
            _state = _state with
            {
                IsActive = true, Completed = false, Success = false,
                CurrentFile = progress.CurrentFile, TotalFiles = progress.TotalFiles,
                FileName = progress.FileName, Percent = progress.Percent,
                Message = $"Kopiere {progress.CurrentFile} von {progress.TotalFiles}: {progress.FileName}",
                Error = null, UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    public void CompleteSuccess(string operationId, int copiedFiles)
    {
        lock (_gate)
        {
            if (!_started || !string.Equals(_state.OperationId, operationId, StringComparison.Ordinal)) return;
            _state = _state with
            {
                IsActive = false, Completed = true, Success = true, Percent = 100,
                Message = copiedFiles == 1 ? "Kopiervorgang erfolgreich" : $"{copiedFiles} Dateien erfolgreich kopiert",
                Error = null, UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    public void CompleteFailure(string operationId, string error)
    {
        lock (_gate)
        {
            if (!_started || !string.Equals(_state.OperationId, operationId, StringComparison.Ordinal)) return;
            _state = _state with
            {
                IsActive = false, Completed = true, Success = false,
                Message = "Kopiervorgang fehlgeschlagen", Error = error, UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    private void Persist()
    {
        AppPaths.EnsureDirectories();
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state));
        File.Move(temp, StatePath, true);
    }
}
