using Microsoft.Data.Sqlite;

namespace MDKOSS.Core.Data;

/// <summary>SQLite connection holder and schema bootstrap.</summary>
public sealed class MdkDatabase : IDisposable
{
    public const int CurrentSchemaVersion = 4;

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
                    fields_json TEXT NOT NULL DEFAULT '{}',
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

                CREATE TABLE IF NOT EXISTS visions (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    camera_device_id TEXT NOT NULL DEFAULT '',
                    pipeline_json TEXT NOT NULL DEFAULT '',
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

            EnsureColumn(conn, "production_orders", "fields_json", "TEXT NOT NULL DEFAULT '{}'");
            EnsureConfigTables(conn);

            cmd.CommandText = "SELECT COUNT(*) FROM schema_version;";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            if (count == 0)
            {
                cmd.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                cmd.ExecuteNonQuery();
            }
            else
            {
                cmd.CommandText = "SELECT MAX(version) FROM schema_version;";
                var version = Convert.ToInt64(cmd.ExecuteScalar() ?? 1L);
                if (version < CurrentSchemaVersion)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "UPDATE schema_version SET version = $v;";
                    cmd.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                    cmd.ExecuteNonQuery();
                }
            }
        });
    }

    /// <summary>
    /// Config-export tables (schema v2): drivers / devices / gpios / axis / platform /
    /// positions / sysconfigs / logs / langs. Recipes already exist from v1.
    /// </summary>
    private static void EnsureConfigTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS drivers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL DEFAULT '',
                type TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                parameters_json TEXT NOT NULL DEFAULT '{}',
                sort_order INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS devices (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL DEFAULT '',
                type TEXT NOT NULL,
                driver_id TEXT,
                enabled INTEGER NOT NULL DEFAULT 1,
                parameters_json TEXT NOT NULL DEFAULT '{}',
                sort_order INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS gpios (
                id TEXT PRIMARY KEY,
                device_id TEXT NOT NULL,
                alias TEXT NOT NULL,
                direction TEXT NOT NULL,
                driver_id TEXT,
                address TEXT,
                label TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL,
                UNIQUE(device_id, alias, direction)
            );

            CREATE TABLE IF NOT EXISTS axis (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL DEFAULT '',
                driver_id TEXT,
                enabled INTEGER NOT NULL DEFAULT 1,
                parameters_json TEXT NOT NULL DEFAULT '{}',
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS platform (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL DEFAULT '',
                kind TEXT,
                driver_id TEXT,
                enabled INTEGER NOT NULL DEFAULT 1,
                parameters_json TEXT NOT NULL DEFAULT '{}',
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS positions (
                id TEXT PRIMARY KEY,
                platform_id TEXT,
                name TEXT NOT NULL DEFAULT '',
                axes_json TEXT NOT NULL DEFAULT '{}',
                source TEXT NOT NULL DEFAULT 'config',
                sort_order INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sysconfigs (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                "group" TEXT NOT NULL DEFAULT 'general',
                remark TEXT NOT NULL DEFAULT '',
                createtime TEXT NOT NULL DEFAULT '',
                updatetime TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                level TEXT NOT NULL DEFAULT 'info',
                category TEXT,
                message TEXT NOT NULL,
                details TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS langs (
                id TEXT PRIMARY KEY,
                locale TEXT NOT NULL DEFAULT 'zh-CN',
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(locale, key)
            );

            CREATE INDEX IF NOT EXISTS idx_gpios_device ON gpios(device_id);
            CREATE INDEX IF NOT EXISTS idx_positions_platform ON positions(platform_id);
            CREATE INDEX IF NOT EXISTS idx_logs_created ON logs(created_at);
            CREATE INDEX IF NOT EXISTS idx_langs_locale ON langs(locale);

            CREATE TABLE IF NOT EXISTS visions (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                camera_device_id TEXT NOT NULL DEFAULT '',
                pipeline_json TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        EnsureColumn(conn, "drivers", "name", "TEXT NOT NULL DEFAULT ''");
        MigrateSysConfigsSchema(conn);
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string ddlType)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddlType}";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// Migrates legacy <c>sysconfigs(category, updated_at)</c> to
    /// <c>key, value, group, remark, createtime, updatetime</c>.
    /// </summary>
    private static void MigrateSysConfigsSchema(SqliteConnection conn)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(sysconfigs)";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                cols.Add(reader.GetString(1));
            }
        }

        if (cols.Count == 0)
        {
            return;
        }

        var hasLegacy = cols.Contains("category") || cols.Contains("updated_at");
        var hasTarget = cols.Contains("group") && cols.Contains("remark")
            && cols.Contains("createtime") && cols.Contains("updatetime");
        if (hasTarget && !hasLegacy)
        {
            return;
        }

        var groupExpr = cols.Contains("group") && cols.Contains("category")
            ? """COALESCE(NULLIF("group", ''), NULLIF(category, ''), 'general')"""
            : cols.Contains("group")
                ? """COALESCE(NULLIF("group", ''), 'general')"""
                : cols.Contains("category")
                    ? "COALESCE(NULLIF(category, ''), 'general')"
                    : "'general'";
        var remarkExpr = cols.Contains("remark") ? "COALESCE(remark, '')" : "''";
        var createExpr = cols.Contains("createtime")
            ? cols.Contains("updated_at")
                ? "COALESCE(NULLIF(createtime, ''), NULLIF(updated_at, ''), '')"
                : "COALESCE(createtime, '')"
            : cols.Contains("updated_at")
                ? "COALESCE(updated_at, '')"
                : "''";
        var updateExpr = cols.Contains("updatetime")
            ? cols.Contains("updated_at")
                ? "COALESCE(NULLIF(updatetime, ''), NULLIF(updated_at, ''), '')"
                : "COALESCE(updatetime, '')"
            : cols.Contains("updated_at")
                ? "COALESCE(updated_at, '')"
                : "''";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE sysconfigs__new (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                "group" TEXT NOT NULL DEFAULT 'general',
                remark TEXT NOT NULL DEFAULT '',
                createtime TEXT NOT NULL DEFAULT '',
                updatetime TEXT NOT NULL DEFAULT ''
            );
            INSERT INTO sysconfigs__new (key, value, "group", remark, createtime, updatetime)
            SELECT key, value, {groupExpr}, {remarkExpr}, {createExpr}, {updateExpr}
            FROM sysconfigs;
            DROP TABLE sysconfigs;
            ALTER TABLE sysconfigs__new RENAME TO sysconfigs;
            """;
        cmd.ExecuteNonQuery();
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
