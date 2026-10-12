using Microsoft.Data.Sqlite;
using PhotoExplorer.Core;

namespace PhotoExplorer.Storage;

public sealed class SqliteThumbnailCache : IThumbnailCache
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1);
    private bool _initialized;
    private const long MaxBytes = 512L * 1024 * 1024;

    public SqliteThumbnailCache(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = true, DefaultTimeout = 10 }.ToString();
    }

    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> action, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                if (!_initialized)
                {
                    using var setup = connection.CreateCommand();
                    setup.CommandText = """
                        PRAGMA journal_mode=WAL;
                        CREATE TABLE IF NOT EXISTS thumbnails (
                            path TEXT NOT NULL, size INTEGER NOT NULL, modified INTEGER NOT NULL,
                            length INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL,
                            data BLOB NOT NULL, accessed INTEGER NOT NULL,
                            PRIMARY KEY(path, size));
                        CREATE INDEX IF NOT EXISTS ix_thumbnails_accessed ON thumbnails(accessed);
                        """;
                    setup.ExecuteNonQuery();
                    _initialized = true;
                }
                return action(connection);
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static string Key(PhotoFile file) => OperatingSystem.IsWindows() ? file.FullPath.ToUpperInvariant() : file.FullPath;

    public Task<Thumbnail?> GetAsync(PhotoFile file, int size, CancellationToken cancellationToken = default) => RunAsync<Thumbnail?>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data,width,height FROM thumbnails WHERE path=$path AND size=$size AND modified=$modified AND length=$length";
        AddSignature(command, file, size);
        Thumbnail? result;
        using (var reader = command.ExecuteReader())
            result = reader.Read() ? new((byte[])reader[0], reader.GetInt32(1), reader.GetInt32(2)) : null;
        if (result is not null)
        {
            command.CommandText = "UPDATE thumbnails SET accessed=$now WHERE path=$path AND size=$size";
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
            command.ExecuteNonQuery();
        }
        return result;
    }, cancellationToken);

    public Task PutAsync(PhotoFile file, int size, Thumbnail thumbnail, CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thumbnails(path,size,modified,length,width,height,data,accessed)
            VALUES($path,$size,$modified,$length,$width,$height,$data,$now)
            ON CONFLICT(path,size) DO UPDATE SET modified=$modified,length=$length,width=$width,height=$height,data=$data,accessed=$now;
            """;
        AddSignature(command, file, size);
        command.Parameters.AddWithValue("$width", thumbnail.Width);
        command.Parameters.AddWithValue("$height", thumbnail.Height);
        command.Parameters.AddWithValue("$data", thumbnail.Bytes);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        command.ExecuteNonQuery();
        // Bound the cache by payload size; evict least recently used entries in one statement.
        command.CommandText = """
            DELETE FROM thumbnails WHERE rowid IN (
                SELECT rowid FROM (
                    SELECT rowid, SUM(LENGTH(data)) OVER (ORDER BY accessed DESC, rowid DESC) AS total FROM thumbnails
                ) WHERE total > $limit);
            """;
        command.Parameters.AddWithValue("$limit", MaxBytes);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }, cancellationToken);

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(LENGTH(data)),0) FROM thumbnails";
        using var reader = command.ExecuteReader();
        reader.Read();
        return new CacheStatistics(reader.GetInt64(0), reader.GetInt64(1));
    }, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => RunAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thumbnails; VACUUM;";
        command.ExecuteNonQuery();
        return true;
    }, cancellationToken);

    private static void AddSignature(SqliteCommand command, PhotoFile file, int size)
    {
        command.Parameters.AddWithValue("$path", Key(file));
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", file.LastWriteUtc.Ticks);
        command.Parameters.AddWithValue("$length", file.Length);
    }
}
