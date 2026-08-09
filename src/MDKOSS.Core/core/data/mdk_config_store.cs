using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MDKOSS.Core.Data;

/// <summary>
/// Exports / imports <see cref="MdkSetting"/> JSON into normalized SQLite config tables
/// (drivers, devices, gpios, axis, platform, positions, sysconfigs, recipes, visions, logs, langs).
/// </summary>
public sealed class MdkConfigStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly (string Locale, string Key, string Value)[] DefaultLangEntries =
    [
        ("zh-CN", "app.title", "MDKOSS 配置"),
        ("zh-CN", "menu.file", "文件"),
        ("zh-CN", "menu.exportDb", "导出到数据库"),
        ("zh-CN", "menu.importDb", "从数据库导入"),
        ("zh-CN", "nav.drivers", "驱动"),
        ("zh-CN", "nav.devices", "设备"),
        ("zh-CN", "nav.gpios", "GPIO"),
        ("zh-CN", "nav.axis", "轴"),
        ("zh-CN", "nav.platform", "平台"),
        ("zh-CN", "nav.positions", "点位"),
        ("zh-CN", "nav.recipes", "配方"),
        ("zh-CN", "nav.visions", "视觉流程"),
        ("zh-CN", "nav.sysconfigs", "系统配置"),
        ("zh-CN", "nav.logs", "日志"),
        ("zh-CN", "nav.langs", "语言"),
        ("en-US", "app.title", "MDKOSS Config"),
        ("en-US", "menu.file", "File"),
        ("en-US", "menu.exportDb", "Export to Database"),
        ("en-US", "menu.importDb", "Import from Database"),
        ("en-US", "nav.drivers", "Drivers"),
        ("en-US", "nav.devices", "Devices"),
        ("en-US", "nav.gpios", "GPIO"),
        ("en-US", "nav.axis", "Axis"),
        ("en-US", "nav.platform", "Platform"),
        ("en-US", "nav.positions", "Positions"),
        ("en-US", "nav.recipes", "Recipes"),
        ("en-US", "nav.visions", "Visions"),
        ("en-US", "nav.sysconfigs", "System"),
        ("en-US", "nav.logs", "Logs"),
        ("en-US", "nav.langs", "Languages"),
    ];

    private readonly MdkDatabase _db;
    private readonly bool _ownsDb;
    private bool _disposed;

    public MdkConfigStore(string dbPath)
    {
        _db = new MdkDatabase(dbPath);
        _ownsDb = true;
    }

    public MdkConfigStore(MdkDatabase database)
    {
        _db = database;
        _ownsDb = false;
    }

    public string DatabasePath => _db.DbPath;

    /// <summary>
    /// Writes the full setting into config tables (replace strategy for setting-owned rows).
    /// Also upserts recipes and mirrors teach points into <c>positions</c>.
    /// </summary>
    public ConfigExportResult ExportSetting(MdkSetting setting, string? sourcePath = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(setting);

        setting.NormalizeSections();

        var now = FormatUtc(DateTime.UtcNow);
        var result = new ConfigExportResult();

        _db.Execute(conn =>
        {
            using var tx = conn.BeginTransaction();

            ClearSettingOwnedTables(conn, tx);

            result.Drivers = InsertDrivers(conn, tx, setting.Drivers, now);
            result.Devices = InsertDevices(conn, tx, setting.Devices, now);
            result.Gpios = InsertGpios(conn, tx, setting.Devices, now);
            result.Axis = InsertAxis(conn, tx, setting.Axes, now);
            result.Platform = InsertPlatform(conn, tx, setting.Platforms, now);
            result.SysConfigs = InsertSysConfigs(conn, tx, setting, now);
            result.Recipes = UpsertRecipes(conn, tx, setting.Recipes, now);
            result.Visions = UpsertVisions(conn, tx, setting.Visions, now);
            result.Positions = MirrorTeachPointsToPositions(conn, tx, now);
            result.Langs = SeedLangsIfEmpty(conn, tx, now);
            AppendLog(
                conn,
                tx,
                "info",
                "config.export",
                $"Exported setting '{setting.ProjectName}' to SQLite.",
                sourcePath is null
                    ? null
                    : JsonSerializer.Serialize(new { sourcePath, result }, JsonOptions),
                now);

            tx.Commit();
        });

        result.DatabasePath = DatabasePath;
        return result;
    }

    /// <summary>Rebuilds an <see cref="MdkSetting"/> from config tables (+ recipes).</summary>
    public MdkSetting ImportSetting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _db.Execute(conn =>
        {
            var setting = new MdkSetting();
            LoadSysConfigs(conn, setting);
            setting.Drivers = LoadDrivers(conn);
            setting.Devices = LoadDevices(conn);
            setting.Axes = LoadAxes(conn);
            setting.Platforms = LoadPlatforms(conn);
            setting.Recipes = LoadRecipes(conn);
            setting.Visions = LoadVisions(conn);
            setting.NormalizeSections();

            AppendLog(
                conn,
                null,
                "info",
                "config.import",
                $"Imported setting '{setting.ProjectName}' from SQLite.",
                null,
                FormatUtc(DateTime.UtcNow));

            return setting;
        });
    }

    public IReadOnlyList<ConfigLogRecord> ListLogs(int limit = 200)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        limit = Math.Clamp(limit, 1, 5000);

        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, level, category, message, details, created_at
                FROM logs
                ORDER BY id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            var list = new List<ConfigLogRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ConfigLogRecord
                {
                    Id = reader.GetInt64(0),
                    Level = reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Message = reader.GetString(3),
                    Details = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CreatedAtUtc = ParseUtc(reader.GetString(5)),
                });
            }

            return list;
        });
    }

    public IReadOnlyList<ConfigLangRecord> ListLangs(string? locale = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, locale, key, value, updated_at
                FROM langs
                """ + (string.IsNullOrWhiteSpace(locale) ? "" : " WHERE locale = $locale") + """
                 ORDER BY locale, key
                """;
            if (!string.IsNullOrWhiteSpace(locale))
            {
                cmd.Parameters.AddWithValue("$locale", locale.Trim());
            }

            var list = new List<ConfigLangRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ConfigLangRecord
                {
                    Id = reader.GetString(0),
                    Locale = reader.GetString(1),
                    Key = reader.GetString(2),
                    Value = reader.GetString(3),
                    UpdatedAtUtc = ParseUtc(reader.GetString(4)),
                });
            }

            return list;
        });
    }

    public ConfigTableCounts CountTables()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _db.Execute(conn =>
        {
            static long Count(SqliteConnection c, string table)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }

            return new ConfigTableCounts
            {
                Drivers = Count(conn, "drivers"),
                Devices = Count(conn, "devices"),
                Gpios = Count(conn, "gpios"),
                Axis = Count(conn, "axis"),
                Platform = Count(conn, "platform"),
                Positions = Count(conn, "positions"),
                SysConfigs = Count(conn, "sysconfigs"),
                Recipes = Count(conn, "recipes"),
                Visions = Count(conn, "visions"),
                Logs = Count(conn, "logs"),
                Langs = Count(conn, "langs"),
            };
        });
    }

    /// <summary>Editable config tables exposed to the Database browser UI.</summary>
    public static IReadOnlyList<string> EditableTableNames { get; } =
    [
        "drivers", "devices", "gpios", "axis", "platform", "positions",
        "sysconfigs", "recipes", "visions", "logs", "langs",
        "production_orders", "teach_point_files", "teach_points",
    ];

    public static bool IsEditableTable(string table) =>
        EditableTableNames.Contains(table, StringComparer.OrdinalIgnoreCase);

    public static string? GetPrimaryKeyColumn(string table) => table.ToLowerInvariant() switch
    {
        "drivers" or "devices" or "gpios" or "axis" or "platform" or "positions"
            or "recipes" or "visions" or "langs" or "production_orders" or "teach_point_files" or "teach_points"
            => "id",
        "sysconfigs" => "key",
        "logs" => "id",
        _ => null,
    };

    /// <summary>Reads all rows from a whitelisted table (string cells for UI editing).</summary>
    public DbTableSnapshot QueryTable(string tableName, int limit = 2000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = RequireEditableTable(tableName);
        limit = Math.Clamp(limit, 1, 10000);
        var pk = GetPrimaryKeyColumn(table);

        return _db.Execute(conn =>
        {
            var columns = PreferColumnOrder(table, ReadColumnNames(conn, table));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table}\" LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<Dictionary<string, string>>(capacity: 64);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    row[name] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i)) ?? "";
                }

                rows.Add(row);
            }

            return new DbTableSnapshot
            {
                TableName = table,
                PrimaryKey = pk,
                Columns = columns,
                Rows = rows,
            };
        });
    }

    /// <summary>Inserts or updates a row by primary key. Returns the PK value used.</summary>
    public string UpsertTableRow(string tableName, IReadOnlyDictionary<string, string> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = RequireEditableTable(tableName);
        var pk = GetPrimaryKeyColumn(table)
                 ?? throw new InvalidOperationException($"表 {table} 没有可识别的主键。");

        var cols = values.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cols.Count == 0)
        {
            throw new InvalidOperationException("没有可写入的列。");
        }

        if (!cols.Contains(pk, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"缺少主键列 '{pk}'。");
        }

        var pkValue = values.First(kv => string.Equals(kv.Key, pk, StringComparison.OrdinalIgnoreCase)).Value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(pkValue) && !string.Equals(table, "logs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"主键 '{pk}' 不能为空。");
        }

        return _db.Execute(conn =>
        {
            var existingCols = new HashSet<string>(ReadColumnNames(conn, table), StringComparer.OrdinalIgnoreCase);
            cols = cols.Where(c => existingCols.Contains(c)).ToList();
            if (!cols.Contains(pk, StringComparer.OrdinalIgnoreCase))
            {
                cols.Insert(0, pk);
            }

            // logs: empty id → INSERT (autoincrement)
            if (string.Equals(table, "logs", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(pkValue))
            {
                var insertCols = cols.Where(c => !string.Equals(c, pk, StringComparison.OrdinalIgnoreCase)).ToList();
                if (insertCols.Count == 0)
                {
                    insertCols.Add("message");
                }

                using var ins = conn.CreateCommand();
                ins.CommandText =
                    $"INSERT INTO \"{table}\" ({string.Join(",", insertCols.Select(c => $"\"{c}\""))}) " +
                    $"VALUES ({string.Join(",", insertCols.Select(c => "$" + c))}); SELECT last_insert_rowid();";
                foreach (var c in insertCols)
                {
                    values.TryGetValue(c, out var v);
                    if (string.Equals(c, "created_at", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(v))
                    {
                        v = DateTime.UtcNow.ToString("O");
                    }

                    if (string.Equals(c, "message", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(v))
                    {
                        v = "";
                    }

                    ins.Parameters.AddWithValue("$" + c, (object?)v ?? "");
                }

                var id = Convert.ToInt64(ins.ExecuteScalar());
                return id.ToString();
            }

            using (var existsCmd = conn.CreateCommand())
            {
                existsCmd.CommandText = $"SELECT 1 FROM \"{table}\" WHERE \"{pk}\" = $pk LIMIT 1;";
                existsCmd.Parameters.AddWithValue("$pk", pkValue);
                var exists = existsCmd.ExecuteScalar() is not null;
                if (exists)
                {
                    var setCols = cols.Where(c => !string.Equals(c, pk, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (setCols.Count == 0)
                    {
                        return pkValue;
                    }

                    using var upd = conn.CreateCommand();
                    upd.CommandText =
                        $"UPDATE \"{table}\" SET {string.Join(", ", setCols.Select(c => $"\"{c}\" = ${c}"))} WHERE \"{pk}\" = $pk;";
                    foreach (var c in setCols)
                    {
                        values.TryGetValue(c, out var v);
                        if (string.Equals(c, "updated_at", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c, "updatetime", StringComparison.OrdinalIgnoreCase))
                        {
                            v = DateTime.UtcNow.ToString("O");
                        }

                    upd.Parameters.AddWithValue("$" + c, (object?)v ?? "");
                }

                    upd.Parameters.AddWithValue("$pk", pkValue);
                    upd.ExecuteNonQuery();
                    return pkValue;
                }
            }

            using (var ins = conn.CreateCommand())
            {
                ins.CommandText =
                    $"INSERT INTO \"{table}\" ({string.Join(",", cols.Select(c => $"\"{c}\""))}) " +
                    $"VALUES ({string.Join(",", cols.Select(c => "$" + c))});";
                foreach (var c in cols)
                {
                    values.TryGetValue(c, out var v);
                    if ((string.Equals(c, "updated_at", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c, "updatetime", StringComparison.OrdinalIgnoreCase))
                        && string.IsNullOrWhiteSpace(v))
                    {
                        v = DateTime.UtcNow.ToString("O");
                    }

                    if ((string.Equals(c, "created_at", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c, "createtime", StringComparison.OrdinalIgnoreCase))
                        && string.IsNullOrWhiteSpace(v))
                    {
                        v = DateTime.UtcNow.ToString("O");
                    }

                    ins.Parameters.AddWithValue("$" + c, (object?)v ?? "");
                }

                ins.ExecuteNonQuery();
            }

            return pkValue;
        });
    }

    public bool DeleteTableRow(string tableName, string primaryKeyValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var table = RequireEditableTable(tableName);
        var pk = GetPrimaryKeyColumn(table)
                 ?? throw new InvalidOperationException($"表 {table} 没有可识别的主键。");
        if (string.IsNullOrWhiteSpace(primaryKeyValue))
        {
            throw new InvalidOperationException("主键值不能为空。");
        }

        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM \"{table}\" WHERE \"{pk}\" = $pk;";
            cmd.Parameters.AddWithValue("$pk", primaryKeyValue.Trim());
            return cmd.ExecuteNonQuery() > 0;
        });
    }

    private static string RequireEditableTable(string tableName)
    {
        var table = (tableName ?? "").Trim();
        if (!IsEditableTable(table))
        {
            throw new InvalidOperationException($"不允许访问表: {tableName}");
        }

        return table.ToLowerInvariant();
    }

    private static List<string> ReadColumnNames(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(1));
        }

        return list;
    }

    private static IReadOnlyList<string> PreferColumnOrder(string table, IReadOnlyList<string> columns)
    {
        if (!string.Equals(table, "sysconfigs", StringComparison.OrdinalIgnoreCase))
        {
            return columns;
        }

        string[] preferred = ["key", "value", "group", "remark", "createtime", "updatetime"];
        var remaining = columns
            .Where(c => !preferred.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var ordered = new List<string>(preferred.Length + remaining.Count);
        foreach (var name in preferred)
        {
            var match = columns.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                ordered.Add(match);
            }
        }

        ordered.AddRange(remaining);
        return ordered;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsDb)
        {
            _db.Dispose();
        }
    }

    // ── export helpers ────────────────────────────────────────────────────

    private static void ClearSettingOwnedTables(SqliteConnection conn, SqliteTransaction tx)
    {
        foreach (var table in new[] { "drivers", "devices", "gpios", "axis", "platform", "sysconfigs", "positions" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table};";
            cmd.ExecuteNonQuery();
        }
    }

    private static int InsertDrivers(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.DriverConfig> drivers,
        string now)
    {
        var n = 0;
        for (var i = 0; i < drivers.Count; i++)
        {
            var d = drivers[i];
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO drivers (id, name, type, enabled, parameters_json, sort_order, updated_at)
                VALUES ($id, $name, $type, $enabled, $parameters_json, $sort_order, $updated_at)
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.Trim());
            cmd.Parameters.AddWithValue("$name", d.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(d.Type) ? "sim" : d.Type.Trim());
            cmd.Parameters.AddWithValue("$enabled", d.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$parameters_json", JsonSerializer.Serialize(d.Parameters, JsonOptions));
            cmd.Parameters.AddWithValue("$sort_order", i);
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int InsertDevices(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.DeviceConfig> devices,
        string now)
    {
        var n = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO devices (id, name, type, driver_id, enabled, parameters_json, sort_order, updated_at)
                VALUES ($id, $name, $type, $driver_id, $enabled, $parameters_json, $sort_order, $updated_at)
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.Trim());
            cmd.Parameters.AddWithValue("$name", d.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(d.Type) ? "gpio" : d.Type.Trim());
            cmd.Parameters.AddWithValue("$driver_id", string.IsNullOrWhiteSpace(d.DriverId) ? (object)DBNull.Value : d.DriverId.Trim());
            cmd.Parameters.AddWithValue("$enabled", d.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$parameters_json", JsonSerializer.Serialize(d.Parameters, JsonOptions));
            cmd.Parameters.AddWithValue("$sort_order", i);
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int InsertGpios(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.DeviceConfig> devices,
        string now)
    {
        var n = 0;
        var sort = 0;
        foreach (var device in devices)
        {
            var type = (device.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (type is not ("gpio" or "vio"))
            {
                continue;
            }

            foreach (var binding in GpioDeviceParameterSet.ParseBindings(device.Parameters, device.DriverId))
            {
                var direction = binding.IsOutput ? "out" : "in";
                var id = $"{device.Id}:{direction}:{binding.Alias}";
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO gpios (id, device_id, alias, direction, driver_id, address, label, sort_order, updated_at)
                    VALUES ($id, $device_id, $alias, $direction, $driver_id, $address, $label, $sort_order, $updated_at)
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$device_id", device.Id);
                cmd.Parameters.AddWithValue("$alias", binding.Alias);
                cmd.Parameters.AddWithValue("$direction", direction);
                cmd.Parameters.AddWithValue("$driver_id", string.IsNullOrWhiteSpace(binding.DriverId) ? (object)DBNull.Value : binding.DriverId);
                cmd.Parameters.AddWithValue("$address", string.IsNullOrWhiteSpace(binding.Address) ? (object)DBNull.Value : binding.Address);
                cmd.Parameters.AddWithValue(
                    "$label",
                    string.IsNullOrWhiteSpace(binding.Label) ? binding.Alias : binding.Label);
                cmd.Parameters.AddWithValue("$sort_order", sort++);
                cmd.Parameters.AddWithValue("$updated_at", now);
                cmd.ExecuteNonQuery();
                n++;
            }

            // Virtual vio points without driver:address still need rows for aliases
            if (type == "vio")
            {
                foreach (var binding in VioDeviceParameterSet.ParseVirtualBindings(device.Parameters))
                {
                    var direction = binding.IsBidirectional ? "vio" : (binding.IsOutput ? "out" : "in");
                    var alias = binding.Alias;
                    var id = $"{device.Id}:{direction}:{alias}";
                    using var exists = conn.CreateCommand();
                    exists.Transaction = tx;
                    exists.CommandText = "SELECT 1 FROM gpios WHERE id = $id";
                    exists.Parameters.AddWithValue("$id", id);
                    if (exists.ExecuteScalar() is not null)
                    {
                        continue;
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO gpios (id, device_id, alias, direction, driver_id, address, label, sort_order, updated_at)
                        VALUES ($id, $device_id, $alias, $direction, $driver_id, $address, $label, $sort_order, $updated_at)
                        """;
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.Parameters.AddWithValue("$device_id", device.Id);
                    cmd.Parameters.AddWithValue("$alias", alias);
                    cmd.Parameters.AddWithValue("$direction", direction);
                    cmd.Parameters.AddWithValue("$driver_id", string.IsNullOrWhiteSpace(device.DriverId) ? (object)DBNull.Value : device.DriverId);
                    cmd.Parameters.AddWithValue("$address", "virtual");
                    cmd.Parameters.AddWithValue("$label", alias);
                    cmd.Parameters.AddWithValue("$sort_order", sort++);
                    cmd.Parameters.AddWithValue("$updated_at", now);
                    cmd.ExecuteNonQuery();
                    n++;
                }
            }
        }

        return n;
    }

    private static int InsertAxis(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.DeviceConfig> devices,
        string now)
    {
        var n = 0;
        foreach (var d in devices.Where(x => AxisDeviceParameterSet.IsAxisFamilyType(x.Type)))
        {
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                continue;
            }

            d.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AxisDeviceParameterSet.SyncKindParameter(d.Parameters, d.Type);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO axis (id, name, driver_id, enabled, parameters_json, updated_at)
                VALUES ($id, $name, $driver_id, $enabled, $parameters_json, $updated_at)
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.Trim());
            cmd.Parameters.AddWithValue("$name", d.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("$driver_id", string.IsNullOrWhiteSpace(d.DriverId) ? (object)DBNull.Value : d.DriverId.Trim());
            cmd.Parameters.AddWithValue("$enabled", d.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$parameters_json", JsonSerializer.Serialize(d.Parameters, JsonOptions));
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int InsertPlatform(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.DeviceConfig> devices,
        string now)
    {
        var n = 0;
        foreach (var d in devices)
        {
            var typeLower = (d.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (!PlatformDeviceParameterSet.IsPlatformFamilyType(typeLower))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(d.Id))
            {
                continue;
            }

            string? kind;
            try
            {
                MPlatformKind? defaultKind = null;
                if (PlatformDeviceParameterSet.TryKindFromDeviceType(typeLower, out var fromType))
                {
                    defaultKind = fromType;
                }

                kind = PlatformKindToken(PlatformDeviceParameterSet.ParseKindOrDefault(d.Parameters, defaultKind));
            }
            catch
            {
                kind = d.Parameters.TryGetValue("kind", out var raw) ? raw : typeLower;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO platform (id, name, kind, driver_id, enabled, parameters_json, updated_at)
                VALUES ($id, $name, $kind, $driver_id, $enabled, $parameters_json, $updated_at)
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.Trim());
            cmd.Parameters.AddWithValue("$name", d.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$driver_id", string.IsNullOrWhiteSpace(d.DriverId) ? (object)DBNull.Value : d.DriverId.Trim());
            cmd.Parameters.AddWithValue("$enabled", d.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$parameters_json", JsonSerializer.Serialize(d.Parameters, JsonOptions));
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int InsertSysConfigs(
        SqliteConnection conn,
        SqliteTransaction tx,
        MdkSetting setting,
        string now)
    {
        var entries = new (string Key, string Value, string Group, string Remark)[]
        {
            ("projectName", setting.ProjectName ?? string.Empty, "general", "工程名称"),
            ("cycleMs", setting.CycleMs.ToString(), "general", "主循环周期(ms)"),
            ("monitoringPrefix", setting.MonitoringPrefix ?? string.Empty, "general", "监控 API 前缀"),
            ("startPage", setting.StartPage ?? string.Empty, "general", "启动页面"),
            ("databasePath", setting.DatabasePath ?? string.Empty, "general", "数据库路径"),
            ("activeRecipeId", setting.ActiveRecipeId ?? string.Empty, "recipe", "当前配方 Id"),
            ("recipeVarKeys", JsonSerializer.Serialize(setting.RecipeVarKeys, JsonOptions), "recipe", "配方变量键列表"),
            ("activeVisionId", setting.ActiveVisionId ?? string.Empty, "vision", "当前视觉流程 Id"),
            ("vars", JsonSerializer.Serialize(setting.Vars, JsonOptions), "vars", "全局变量 JSON"),
            ("tasks", JsonSerializer.Serialize(setting.Tasks, JsonOptions), "tasks", "任务列表 JSON"),
        };

        foreach (var (key, value, group, remark) in entries)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO sysconfigs (key, value, "group", remark, createtime, updatetime)
                VALUES ($key, $value, $group, $remark, $createtime, $updatetime)
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.Parameters.AddWithValue("$group", group);
            cmd.Parameters.AddWithValue("$remark", remark);
            cmd.Parameters.AddWithValue("$createtime", now);
            cmd.Parameters.AddWithValue("$updatetime", now);
            cmd.ExecuteNonQuery();
        }

        return entries.Length;
    }

    private static int UpsertRecipes(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.RecipeConfig> recipes,
        string now)
    {
        var n = 0;
        foreach (var r in recipes)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO recipes (id, name, description, vars_json, created_at, updated_at)
                VALUES ($id, $name, $description, $vars_json, $created_at, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    vars_json = excluded.vars_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$id", r.Id.Trim());
            cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(r.Name) ? r.Id.Trim() : r.Name.Trim());
            cmd.Parameters.AddWithValue("$description", (object?)r.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vars_json", JsonSerializer.Serialize(r.Vars, JsonOptions));
            cmd.Parameters.AddWithValue("$created_at", now);
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int UpsertVisions(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<MdkSetting.VisionConfig> visions,
        string now)
    {
        var n = 0;
        foreach (var v in visions)
        {
            if (string.IsNullOrWhiteSpace(v.Id))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO visions (id, name, description, camera_device_id, pipeline_json, created_at, updated_at)
                VALUES ($id, $name, $description, $camera_device_id, $pipeline_json, $created_at, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    camera_device_id = excluded.camera_device_id,
                    pipeline_json = excluded.pipeline_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$id", v.Id.Trim());
            cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(v.Name) ? v.Id.Trim() : v.Name.Trim());
            cmd.Parameters.AddWithValue("$description", (object?)v.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$camera_device_id", v.CameraDeviceId ?? string.Empty);
            cmd.Parameters.AddWithValue(
                "$pipeline_json",
                string.IsNullOrWhiteSpace(v.PipelineJson)
                    ? Vision.VisionDocument.CreateBasicInspectPipeline().ToJson()
                    : v.PipelineJson);
            cmd.Parameters.AddWithValue("$created_at", now);
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static int MirrorTeachPointsToPositions(SqliteConnection conn, SqliteTransaction tx, string now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO positions (id, platform_id, name, axes_json, source, sort_order, updated_at)
            SELECT
                tp.id,
                tf.platform_id,
                CASE WHEN tp.name = '' THEN tp.point_id ELSE tp.name END,
                tp.axes_json,
                'teach',
                tp.sort_order,
                $updated_at
            FROM teach_points tp
            INNER JOIN teach_point_files tf ON tf.id = tp.file_id
            """;
        cmd.Parameters.AddWithValue("$updated_at", now);
        return cmd.ExecuteNonQuery();
    }

    private static string PlatformKindToken(MPlatformKind kind) => kind.ToConfigToken();

    private static int SeedLangsIfEmpty(SqliteConnection conn, SqliteTransaction tx, string now)
    {
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.Transaction = tx;
            countCmd.CommandText = "SELECT COUNT(*) FROM langs;";
            var existing = Convert.ToInt64(countCmd.ExecuteScalar());
            if (existing > 0)
            {
                return (int)existing;
            }
        }

        var n = 0;
        foreach (var (locale, key, value) in DefaultLangEntries)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO langs (id, locale, key, value, updated_at)
                VALUES ($id, $locale, $key, $value, $updated_at)
                """;
            cmd.Parameters.AddWithValue("$id", $"{locale}:{key}");
            cmd.Parameters.AddWithValue("$locale", locale);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.Parameters.AddWithValue("$updated_at", now);
            cmd.ExecuteNonQuery();
            n++;
        }

        return n;
    }

    private static void AppendLog(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string level,
        string? category,
        string message,
        string? details,
        string now)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = """
            INSERT INTO logs (level, category, message, details, created_at)
            VALUES ($level, $category, $message, $details, $created_at)
            """;
        cmd.Parameters.AddWithValue("$level", level);
        cmd.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$message", message);
        cmd.Parameters.AddWithValue("$details", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", now);
        cmd.ExecuteNonQuery();
    }

    // ── import helpers ────────────────────────────────────────────────────

    private static void LoadSysConfigs(SqliteConnection conn, MdkSetting setting)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM sysconfigs;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            switch (key)
            {
                case "projectName":
                    setting.ProjectName = value;
                    break;
                case "cycleMs" when int.TryParse(value, out var cycle):
                    setting.CycleMs = cycle;
                    break;
                case "monitoringPrefix":
                    setting.MonitoringPrefix = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "startPage":
                    setting.StartPage = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "databasePath":
                    setting.DatabasePath = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "activeRecipeId":
                    setting.ActiveRecipeId = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "activeVisionId":
                    setting.ActiveVisionId = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "recipeVarKeys":
                    setting.RecipeVarKeys = DeserializeOrDefault(value, new List<string>());
                    break;
                case "vars":
                    setting.Vars = DeserializeOrDefault(value, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                    break;
                case "tasks":
                    setting.Tasks = DeserializeOrDefault(value, new List<MdkSetting.TaskConfig>());
                    break;
            }
        }
    }

    private static List<MdkSetting.DriverConfig> LoadDrivers(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, type, enabled, parameters_json
            FROM drivers
            ORDER BY sort_order, id COLLATE NOCASE
            """;
        var list = new List<MdkSetting.DriverConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MdkSetting.DriverConfig
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Type = reader.GetString(2),
                Enabled = reader.GetInt64(3) != 0,
                Parameters = DeserializeOrDefault(
                    reader.GetString(4),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            });
        }

        return list;
    }

    private static List<MdkSetting.DeviceConfig> LoadDevices(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, type, driver_id, enabled, parameters_json
            FROM devices
            ORDER BY sort_order, id COLLATE NOCASE
            """;
        var list = new List<MdkSetting.DeviceConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MdkSetting.DeviceConfig
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = reader.GetString(2),
                DriverId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Enabled = reader.GetInt64(4) != 0,
                Parameters = DeserializeOrDefault(
                    reader.GetString(5),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            });
        }

        return list;
    }

    private static List<MdkSetting.DeviceConfig> LoadAxes(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, driver_id, enabled, parameters_json
            FROM axis
            ORDER BY id COLLATE NOCASE
            """;
        var list = new List<MdkSetting.DeviceConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var parameters = DeserializeOrDefault(
                reader.GetString(4),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            var kindToken = AxisDeviceParameterSet.GetKindToken(parameters);
            list.Add(new MdkSetting.DeviceConfig
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                // Prefer geometry token so UI Type combo shows linear/rotary after DB round-trip.
                Type = kindToken,
                DriverId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Enabled = reader.GetInt64(3) != 0,
                Parameters = parameters,
            });
        }

        return list;
    }

    private static List<MdkSetting.DeviceConfig> LoadPlatforms(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, kind, driver_id, enabled, parameters_json
            FROM platform
            ORDER BY id COLLATE NOCASE
            """;
        var list = new List<MdkSetting.DeviceConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kind = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
            var type = string.IsNullOrWhiteSpace(kind) ? "platform" : kind;
            list.Add(new MdkSetting.DeviceConfig
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Type = type,
                DriverId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Enabled = reader.GetInt64(4) != 0,
                Parameters = DeserializeOrDefault(
                    reader.GetString(5),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            });
        }

        return list;
    }

    private static List<MdkSetting.RecipeConfig> LoadRecipes(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, description, vars_json
            FROM recipes
            ORDER BY name COLLATE NOCASE
            """;
        var list = new List<MdkSetting.RecipeConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MdkSetting.RecipeConfig
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Vars = DeserializeOrDefault(
                    reader.GetString(3),
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)),
            });
        }

        return list;
    }

    private static List<MdkSetting.VisionConfig> LoadVisions(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, description, camera_device_id, pipeline_json
            FROM visions
            ORDER BY name COLLATE NOCASE
            """;
        var list = new List<MdkSetting.VisionConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MdkSetting.VisionConfig
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                CameraDeviceId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                PipelineJson = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            });
        }

        return list;
    }

    private static T DeserializeOrDefault<T>(string json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string FormatUtc(DateTime utc) =>
        utc.Kind == DateTimeKind.Utc
            ? utc.ToString("O")
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O");

    private static DateTime ParseUtc(string raw) =>
        DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
}

public sealed class ConfigExportResult
{
    public string DatabasePath { get; set; } = string.Empty;
    public int Drivers { get; set; }
    public int Devices { get; set; }
    public int Gpios { get; set; }
    public int Axis { get; set; }
    public int Platform { get; set; }
    public int Positions { get; set; }
    public int SysConfigs { get; set; }
    public int Recipes { get; set; }
    public int Visions { get; set; }
    public int Langs { get; set; }

    public override string ToString() =>
        $"drivers={Drivers}, devices={Devices}, gpios={Gpios}, axis={Axis}, platform={Platform}, " +
        $"positions={Positions}, sysconfigs={SysConfigs}, recipes={Recipes}, visions={Visions}, langs={Langs}";
}

public sealed class ConfigTableCounts
{
    public long Drivers { get; set; }
    public long Devices { get; set; }
    public long Gpios { get; set; }
    public long Axis { get; set; }
    public long Platform { get; set; }
    public long Positions { get; set; }
    public long SysConfigs { get; set; }
    public long Recipes { get; set; }
    public long Visions { get; set; }
    public long Logs { get; set; }
    public long Langs { get; set; }
}

public sealed class ConfigLogRecord
{
    public long Id { get; set; }
    public string Level { get; set; } = "info";
    public string? Category { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ConfigLangRecord
{
    public string Id { get; set; } = string.Empty;
    public string Locale { get; set; } = "zh-CN";
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Snapshot of one SQLite config table for the Database browser UI.</summary>
public sealed class DbTableSnapshot
{
    public string TableName { get; set; } = string.Empty;
    public string? PrimaryKey { get; set; }
    public IReadOnlyList<string> Columns { get; set; } = [];
    public IReadOnlyList<Dictionary<string, string>> Rows { get; set; } = [];
}

