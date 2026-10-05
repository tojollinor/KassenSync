using KassenSync.Core.Models;
using Microsoft.Data.Sqlite;

namespace KassenSync.Core.Services;

public sealed class IndexDatabase
{
    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = AppPaths.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS indexed_files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                full_source_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                detected_at_utc TEXT NOT NULL,
                last_copied_at_utc TEXT NULL,
                copy_count INTEGER NOT NULL DEFAULT 0,
                status INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                UNIQUE(file_name, sha256)
            );
            CREATE INDEX IF NOT EXISTS ix_indexed_files_status ON indexed_files(status);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_detected ON indexed_files(detected_at_utc DESC);
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string fileName, string sha256, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM indexed_files WHERE file_name = $name AND sha256 = $sha LIMIT 1;";
        command.Parameters.AddWithValue("$name", fileName);
        command.Parameters.AddWithValue("$sha", sha256);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<long?> AddIfNewAsync(IndexedFile file, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO indexed_files
            (file_name, relative_path, full_source_path, sha256, size_bytes, detected_at_utc, status)
            VALUES ($name, $relative, $full, $sha, $size, $detected, $status);
            SELECT CASE WHEN changes() = 1 THEN last_insert_rowid() ELSE NULL END;
            """;
        command.Parameters.AddWithValue("$name", file.FileName);
        command.Parameters.AddWithValue("$relative", file.RelativePath);
        command.Parameters.AddWithValue("$full", file.FullSourcePath);
        command.Parameters.AddWithValue("$sha", file.Sha256);
        command.Parameters.AddWithValue("$size", file.SizeBytes);
        command.Parameters.AddWithValue("$detected", file.DetectedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", (int)file.Status);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<IndexedFile>> GetAutomaticQueueAsync(int limit = 100, CancellationToken cancellationToken = default)
        => await QueryAsync("WHERE status IN ($indexed, $waiting) ORDER BY detected_at_utc ASC LIMIT $limit", command =>
        {
            command.Parameters.AddWithValue("$indexed", (int)FileTransferStatus.Indexed);
            command.Parameters.AddWithValue("$waiting", (int)FileTransferStatus.WaitingForTarget);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        }, cancellationToken);

    public async Task<IReadOnlyList<IndexedFile>> GetByIdsAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().Where(x => x > 0).ToArray();
        if (distinctIds.Length == 0) return Array.Empty<IndexedFile>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var placeholders = new List<string>();
        for (var i = 0; i < distinctIds.Length; i++)
        {
            var p = $"$id{i}";
            placeholders.Add(p);
            command.Parameters.AddWithValue(p, distinctIds[i]);
        }
        command.CommandText = SelectColumns + $" WHERE id IN ({string.Join(",", placeholders)}) ORDER BY detected_at_utc ASC;";
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<IndexedFile>> GetAllAsync(CancellationToken cancellationToken = default)
        => await QueryAsync("ORDER BY detected_at_utc DESC", null, cancellationToken);

    public async Task QueueForRecopyAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().Where(x => x > 0).ToArray();
        if (distinctIds.Length == 0) return;
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var id in distinctIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE indexed_files SET status = $status, last_error = NULL WHERE id = $id;";
            command.Parameters.AddWithValue("$status", (int)FileTransferStatus.Indexed);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetStatusAsync(long id, FileTransferStatus status, string? error = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE indexed_files SET status = $status, last_error = $error WHERE id = $id;";
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkCopiedAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE indexed_files SET status = $status, last_error = NULL,
            last_copied_at_utc = $copied, copy_count = copy_count + 1 WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", (int)FileTransferStatus.Copied);
        command.Parameters.AddWithValue("$copied", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectColumns = "SELECT id, file_name, relative_path, full_source_path, sha256, size_bytes, detected_at_utc, last_copied_at_utc, copy_count, status, last_error FROM indexed_files";

    private async Task<IReadOnlyList<IndexedFile>> QueryAsync(string suffix, Action<SqliteCommand>? configure, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " " + suffix + ";";
        configure?.Invoke(command);
        return await ReadAllAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IndexedFile>> ReadAllAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<IndexedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadIndexedFile(reader));
        return result;
    }

    private static IndexedFile ReadIndexedFile(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0), FileName = reader.GetString(1), RelativePath = reader.GetString(2), FullSourcePath = reader.GetString(3),
        Sha256 = reader.GetString(4), SizeBytes = reader.GetInt64(5),
        DetectedAtUtc = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
        LastCopiedAtUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
        CopyCount = reader.GetInt32(8), Status = (FileTransferStatus)reader.GetInt32(9), LastError = reader.IsDBNull(10) ? null : reader.GetString(10)
    };
}
