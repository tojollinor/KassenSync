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

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableExistsAsync(connection, "indexed_files", cancellationToken))
        {
            await CreateCurrentSchemaAsync(connection, cancellationToken);
            return;
        }

        if (!await ColumnExistsAsync(connection, "indexed_files", "job_id", cancellationToken))
            await MigrateLegacySchemaAsync(connection, cancellationToken);

        await EnsureIndexesAsync(connection, cancellationToken);
    }

    public Task<bool> ExistsAsync(string fileName, string sha256, CancellationToken cancellationToken = default)
        => ExistsAsync(SyncJob.LegacyJobId, SyncSide.A, fileName, sha256, cancellationToken);

    public async Task<bool> ExistsAsync(
        string jobId,
        SyncSide sourceSide,
        string fileName,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM indexed_files
            WHERE job_id = $job
              AND source_side = $side
              AND file_name = $name
              AND sha256 = $sha
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job", jobId);
        command.Parameters.AddWithValue("$side", (int)sourceSide);
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
            (job_id, source_side, file_name, relative_path, full_source_path, sha256,
             size_bytes, detected_at_utc, status, last_error)
            VALUES ($job, $side, $name, $relative, $full, $sha, $size, $detected, $status, $error);
            SELECT CASE WHEN changes() = 1 THEN last_insert_rowid() ELSE NULL END;
            """;
        command.Parameters.AddWithValue("$job", string.IsNullOrWhiteSpace(file.JobId) ? SyncJob.LegacyJobId : file.JobId);
        command.Parameters.AddWithValue("$side", (int)file.SourceSide);
        command.Parameters.AddWithValue("$name", file.FileName);
        command.Parameters.AddWithValue("$relative", file.RelativePath);
        command.Parameters.AddWithValue("$full", file.FullSourcePath);
        command.Parameters.AddWithValue("$sha", file.Sha256);
        command.Parameters.AddWithValue("$size", file.SizeBytes);
        command.Parameters.AddWithValue("$detected", file.DetectedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", (int)file.Status);
        command.Parameters.AddWithValue("$error", (object?)file.LastError ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<IndexedFile>> GetAutomaticQueueAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
        => await QueryAsync(
            "WHERE status IN ($indexed, $waiting) ORDER BY detected_at_utc ASC LIMIT $limit",
            command =>
            {
                command.Parameters.AddWithValue("$indexed", (int)FileTransferStatus.Indexed);
                command.Parameters.AddWithValue("$waiting", (int)FileTransferStatus.WaitingForTarget);
                command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
            },
            cancellationToken);

    public async Task<IReadOnlyList<IndexedFile>> GetAutomaticQueueForJobAsync(
        string jobId,
        int limit = 100,
        CancellationToken cancellationToken = default)
        => await QueryAsync(
            "WHERE job_id = $job AND status IN ($indexed, $waiting) ORDER BY detected_at_utc ASC LIMIT $limit",
            command =>
            {
                command.Parameters.AddWithValue("$job", jobId);
                command.Parameters.AddWithValue("$indexed", (int)FileTransferStatus.Indexed);
                command.Parameters.AddWithValue("$waiting", (int)FileTransferStatus.WaitingForTarget);
                command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
            },
            cancellationToken);

    public async Task<IReadOnlyList<IndexedFile>> GetByIdsAsync(
        IEnumerable<long> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().Where(x => x > 0).ToArray();
        if (distinctIds.Length == 0)
            return Array.Empty<IndexedFile>();

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

        command.CommandText =
            SelectColumns + $" WHERE id IN ({string.Join(",", placeholders)}) ORDER BY detected_at_utc ASC;";
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<IndexedFile>> GetAllAsync(CancellationToken cancellationToken = default)
        => await QueryAsync("ORDER BY detected_at_utc DESC", null, cancellationToken);

    public async Task<IReadOnlyList<IndexedFile>> GetAllForJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
        => await QueryAsync(
            "WHERE job_id = $job ORDER BY detected_at_utc DESC",
            command => command.Parameters.AddWithValue("$job", jobId),
            cancellationToken);

    public async Task QueueForRecopyAsync(
        IEnumerable<long> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Distinct().Where(x => x > 0).ToArray();
        if (distinctIds.Length == 0)
            return;

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var id in distinctIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                "UPDATE indexed_files SET status = $status, last_error = NULL WHERE id = $id;";
            command.Parameters.AddWithValue("$status", (int)FileTransferStatus.Indexed);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetStatusAsync(
        long id,
        FileTransferStatus status,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE indexed_files SET status = $status, last_error = $error WHERE id = $id;";
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
            UPDATE indexed_files
            SET status = $status,
                last_error = NULL,
                last_copied_at_utc = $copied,
                copy_count = copy_count + 1
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", (int)FileTransferStatus.Copied);
        command.Parameters.AddWithValue("$copied", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetSyncBaselineAsync(
        string jobId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sha256
            FROM sync_baselines
            WHERE job_id = $job AND relative_path = $relative
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job", jobId);
        command.Parameters.AddWithValue("$relative", NormalizeRelativePath(relativePath));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    public async Task SetSyncBaselineAsync(
        string jobId,
        string relativePath,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync_baselines(job_id, relative_path, sha256, updated_at_utc)
            VALUES ($job, $relative, $sha, $updated)
            ON CONFLICT(job_id, relative_path)
            DO UPDATE SET sha256 = excluded.sha256, updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$job", jobId);
        command.Parameters.AddWithValue("$relative", NormalizeRelativePath(relativePath));
        command.Parameters.AddWithValue("$sha", sha256);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static async Task CreateCurrentSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CurrentTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureIndexesAsync(connection, cancellationToken);
    }

    private static async Task MigrateLegacySchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                DROP TABLE IF EXISTS indexed_files_v3;

                CREATE TABLE indexed_files_v3 (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    job_id TEXT NOT NULL,
                    source_side INTEGER NOT NULL DEFAULT 0,
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
                    UNIQUE(job_id, source_side, file_name, sha256)
                );

                INSERT INTO indexed_files_v3
                (id, job_id, source_side, file_name, relative_path, full_source_path, sha256,
                 size_bytes, detected_at_utc, last_copied_at_utc, copy_count, status, last_error)
                SELECT
                    id,
                    $legacyJob,
                    0,
                    file_name,
                    relative_path,
                    full_source_path,
                    sha256,
                    size_bytes,
                    detected_at_utc,
                    last_copied_at_utc,
                    copy_count,
                    status,
                    last_error
                FROM indexed_files;

                DROP TABLE indexed_files;
                ALTER TABLE indexed_files_v3 RENAME TO indexed_files;
                """;
            command.Parameters.AddWithValue("$legacyJob", SyncJob.LegacyJobId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await EnsureIndexesAsync(connection, cancellationToken);
    }

    private static async Task EnsureIndexesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_indexed_files_job_status
                ON indexed_files(job_id, status);
            CREATE INDEX IF NOT EXISTS ix_indexed_files_detected
                ON indexed_files(detected_at_utc DESC);

            CREATE TABLE IF NOT EXISTS sync_baselines (
                job_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(job_id, relative_path)
            );

            PRAGMA user_version = 4;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table.Replace("]", "]]")}]);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private const string CurrentTableSql = """
        CREATE TABLE indexed_files (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id TEXT NOT NULL,
            source_side INTEGER NOT NULL DEFAULT 0,
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
            UNIQUE(job_id, source_side, file_name, sha256)
        );
        """;

    private const string SelectColumns =
        "SELECT id, job_id, source_side, file_name, relative_path, full_source_path, sha256, " +
        "size_bytes, detected_at_utc, last_copied_at_utc, copy_count, status, last_error FROM indexed_files";

    private async Task<IReadOnlyList<IndexedFile>> QueryAsync(
        string suffix,
        Action<SqliteCommand>? configure,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " " + suffix + ";";
        configure?.Invoke(command);
        return await ReadAllAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IndexedFile>> ReadAllAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<IndexedFile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadIndexedFile(reader));

        return result;
    }

    private static IndexedFile ReadIndexedFile(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        JobId = reader.GetString(1),
        SourceSide = (SyncSide)reader.GetInt32(2),
        FileName = reader.GetString(3),
        RelativePath = reader.GetString(4),
        FullSourcePath = reader.GetString(5),
        Sha256 = reader.GetString(6),
        SizeBytes = reader.GetInt64(7),
        DetectedAtUtc = DateTime.Parse(
            reader.GetString(8),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        LastCopiedAtUtc = reader.IsDBNull(9)
            ? null
            : DateTime.Parse(
                reader.GetString(9),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind),
        CopyCount = reader.GetInt32(10),
        Status = (FileTransferStatus)reader.GetInt32(11),
        LastError = reader.IsDBNull(12) ? null : reader.GetString(12)
    };
}
