using Microsoft.Data.Sqlite;

namespace MDKOSS.Core.Data;

/// <summary>SQLite connection holder and schema bootstrap.</summary>
public sealed class MdkDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private bool _disposed;

    public MdkDatabase(string dbPath)
    {
        var fullPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString;

        InitializeSchema();
    }

    public string DbPath =>
        new SqliteConnectionStringBuilder(_connectionString).DataSource;

    internal SqliteConnection OpenConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    internal void Execute(Action<SqliteConnection> action)
    {
        lock (_gate)
        {
            using var conn = OpenConnection();
            action(conn);
        }
    }

    internal T Execute<T>(Func<SqliteConnection, T> action)
    {
        lock (_gate)
        {
            using var conn = OpenConnection();
            return action(conn);
        }
    }

    private void InitializeSchema()
    {
        Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_version (
                    version INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS production_orders (
                    id TEXT PRIMARY KEY,
                    product TEXT NOT NULL DEFAULT '',
                    qty INTEGER NOT NULL DEFAULT 1,
                    status TEXT NOT NULL DEFAULT 'pending',
                    progress REAL NOT NULL DEFAULT 0,
                    recipe_id TEXT,
                    priority INTEGER NOT NULL DEFAULT 0,
                    notes TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS recipes (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    vars_json TEXT NOT NULL DEFAULT '{}',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS teach_point_files (
                    id TEXT PRIMARY KEY,
                    platform_id TEXT NOT NULL,
                    name TEXT NOT NULL DEFAULT 'default',
                    platform_kind TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS teach_points (
                    id TEXT PRIMARY KEY,
                    file_id TEXT NOT NULL REFERENCES teach_point_files(id) ON DELETE CASCADE,
                    point_id TEXT NOT NULL,
                    name TEXT NOT NULL DEFAULT '',
                    axes_json TEXT NOT NULL DEFAULT '{}',
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    UNIQUE(file_id, point_id)
                );

                CREATE INDEX IF NOT EXISTS idx_orders_status ON production_orders(status);
                CREATE INDEX IF NOT EXISTS idx_teach_points_file ON teach_points(file_id);
                CREATE INDEX IF NOT EXISTS idx_teach_point_files_platform ON teach_point_files(platform_id);
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT COUNT(*) FROM schema_version;";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            if (count == 0)
            {
                cmd.CommandText = "INSERT INTO schema_version (version) VALUES (1);";
                cmd.ExecuteNonQuery();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SqliteConnection.ClearAllPools();
    }
}
