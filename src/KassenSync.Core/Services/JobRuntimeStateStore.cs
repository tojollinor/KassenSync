using System.Text.Json;
using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class JobRuntimeStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, JobRuntimeState> _states =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public static string StatePath =>
        Path.Combine(AppPaths.DataDirectory, "job-runtime-state.json");

    public IReadOnlyList<JobRuntimeState> GetAll()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _states.Values
                .OrderBy(x => x.JobId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public JobRuntimeState Get(string jobId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return GetOrCreate(jobId);
        }
    }

    public bool IsWaiting(string jobId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return GetOrCreate(jobId).WaitingForUserConfirmation;
        }
    }

    public void UpdateTargetPresence(string jobId, bool targetPresent)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var current = GetOrCreate(jobId);

            if (current.TargetPresent == targetPresent)
                return;

            var declined = current.DeclinedForCurrentPresence;

            // Sobald der Stick wieder entfernt wird, darf beim nächsten
            // Einstecken erneut nachgefragt werden.
            if (!targetPresent)
                declined = false;

            _states[jobId] = current with
            {
                TargetPresent = targetPresent,
                DeclinedForCurrentPresence = declined,
                UpdatedAtUtc = DateTime.UtcNow
            };

            Persist();
        }
    }

    public void Defer(string jobId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var current = GetOrCreate(jobId);
            _states[jobId] = current with
            {
                WaitingForUserConfirmation = true,
                DeclinedForCurrentPresence = false,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    public void Resume(string jobId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var current = GetOrCreate(jobId);
            _states[jobId] = current with
            {
                WaitingForUserConfirmation = false,
                DeclinedForCurrentPresence = false,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    public void Decline(string jobId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var current = GetOrCreate(jobId);
            _states[jobId] = current with
            {
                WaitingForUserConfirmation = true,
                DeclinedForCurrentPresence = true,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Persist();
        }
    }

    private JobRuntimeState GetOrCreate(string jobId)
    {
        if (_states.TryGetValue(jobId, out var state))
            return state;

        state = new JobRuntimeState(
            jobId,
            WaitingForUserConfirmation: false,
            DeclinedForCurrentPresence: false,
            TargetPresent: false,
            UpdatedAtUtc: DateTime.UtcNow);

        _states[jobId] = state;
        return state;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        AppPaths.EnsureDirectories();

        if (!File.Exists(StatePath))
            return;

        try
        {
            var json = File.ReadAllText(StatePath);
            var states = JsonSerializer.Deserialize<List<JobRuntimeState>>(json, JsonOptions);
            if (states is null)
                return;

            foreach (var state in states)
            {
                if (!string.IsNullOrWhiteSpace(state.JobId))
                    _states[state.JobId] = state;
            }
        }
        catch
        {
            // Defekte Laufzeitdateien dürfen den Dienst nicht am Start hindern.
        }
    }

    private void Persist()
    {
        AppPaths.EnsureDirectories();
        var temp = StatePath + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(_states.Values.ToArray(), JsonOptions));
        File.Move(temp, StatePath, true);
    }
}
