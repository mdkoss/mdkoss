using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MDKOSS.Core.Data;

/// <summary>
/// SQLite-backed persistence for production orders, recipes, teach points, and calibration.
/// </summary>
public sealed class MdkDataStore : IDisposable
{
    public const string OrderListVarKey = "order.list";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MdkDatabase _db;
    private bool _disposed;

    public MdkDataStore(string dbPath)
    {
        _db = new MdkDatabase(dbPath);
    }

    public MdkDataStore(MdkDatabase database)
    {
        _db = database;
    }

    public string DatabasePath => _db.DbPath;

    // ── Production orders (排单) ─────────────────────────────────────────

    public IReadOnlyList<ProductionOrderRecord> ListOrders(string? status = null)
    {
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, product, qty, status, progress, recipe_id, priority, notes, fields_json, created_at, updated_at
                FROM production_orders
                """ + (string.IsNullOrWhiteSpace(status) ? "" : " WHERE status = $status") + """
                 ORDER BY priority DESC, created_at ASC
                """;
            if (!string.IsNullOrWhiteSpace(status))
            {
                cmd.Parameters.AddWithValue("$status", status.Trim());
            }

            var list = new List<ProductionOrderRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadOrder(reader));
            }

            return list;
        });
    }

    public bool TryGetOrder(string id, out ProductionOrderRecord? order)
    {
        order = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, product, qty, status, progress, recipe_id, priority, notes, fields_json, created_at, updated_at
                FROM production_orders WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadOrder(reader) : null;
        });

        return order is not null;
    }

    public bool TryUpsertOrder(ProductionOrderRecord order, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(order.Id))
        {
            error = "order_id_required";
            return false;
        }

        order.AbsorbExtensionData();

        var now = DateTime.UtcNow;
        if (order.CreatedAtUtc == default)
        {
            order.CreatedAtUtc = now;
        }

        order.UpdatedAtUtc = now;
        order.Id = order.Id.Trim();
        order.Fields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO production_orders
                    (id, product, qty, status, progress, recipe_id, priority, notes, fields_json, created_at, updated_at)
                VALUES
                    ($id, $product, $qty, $status, $progress, $recipe_id, $priority, $notes, $fields_json, $created_at, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    product = excluded.product,
                    qty = excluded.qty,
                    status = excluded.status,
                    progress = excluded.progress,
                    recipe_id = excluded.recipe_id,
                    priority = excluded.priority,
                    notes = excluded.notes,
                    fields_json = excluded.fields_json,
                    updated_at = excluded.updated_at
                """;
            BindOrder(cmd, order);
            cmd.ExecuteNonQuery();
        });

        return true;
    }

    public bool TryDeleteOrder(string id, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "order_id_required";
            return false;
        }

        var deleted = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM production_orders WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id.Trim());
            return cmd.ExecuteNonQuery();
        });

        if (deleted == 0)
        {
            error = "order_not_found";
            return false;
        }

        return true;
    }

    public string SerializeOrdersForVar() =>
        JsonSerializer.Serialize(ListOrders(), JsonOptions);

    /// <summary>
    /// When the orders table is empty, seeds rows from setting <c>order.list</c> JSON so sample/demo
    /// configs persist into SQLite on first run.
    /// </summary>
    public int SyncOrdersFromSettingVars(IReadOnlyDictionary<string, object?> vars)
    {
        if (ListOrders().Count > 0)
        {
            return 0;
        }

        if (!vars.TryGetValue(OrderListVarKey, out var raw) || raw is null)
        {
            return 0;
        }

        var text = raw switch
        {
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.GetRawText(),
            _ => raw.ToString() ?? "",
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        List<ProductionOrderRecord>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<ProductionOrderRecord>>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return 0;
        }

        if (list is null || list.Count == 0)
        {
            return 0;
        }

        var seeded = 0;
        foreach (var order in list)
        {
            if (string.IsNullOrWhiteSpace(order.Id))
            {
                continue;
            }

            if (TryUpsertOrder(order, out _))
            {
                seeded++;
            }
        }

        return seeded;
    }

    // ── Recipes (配方) ───────────────────────────────────────────────────

    public IReadOnlyList<RecipeRecord> ListRecipes()
    {
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, description, vars_json, created_at, updated_at
                FROM recipes ORDER BY name COLLATE NOCASE
                """;
            var list = new List<RecipeRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadRecipe(reader));
            }

            return list;
        });
    }

    public bool TryGetRecipe(string id, out RecipeRecord? recipe)
    {
        recipe = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, description, vars_json, created_at, updated_at
                FROM recipes WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadRecipe(reader) : null;
        });

        return recipe is not null;
    }

    public bool TryUpsertRecipe(RecipeRecord recipe, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(recipe.Id))
        {
            error = "recipe_id_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            error = "recipe_name_required";
            return false;
        }

        var now = DateTime.UtcNow;
        if (recipe.CreatedAtUtc == default)
        {
            recipe.CreatedAtUtc = now;
        }

        recipe.UpdatedAtUtc = now;
        recipe.Id = recipe.Id.Trim();
        recipe.Name = recipe.Name.Trim();

        _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO recipes (id, name, description, vars_json, created_at, updated_at)
                VALUES ($id, $name, $description, $vars_json, $created_at, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    vars_json = excluded.vars_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$id", recipe.Id);
            cmd.Parameters.AddWithValue("$name", recipe.Name);
            cmd.Parameters.AddWithValue("$description", (object?)recipe.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vars_json", JsonSerializer.Serialize(recipe.Vars, JsonOptions));
            cmd.Parameters.AddWithValue("$created_at", FormatUtc(recipe.CreatedAtUtc));
            cmd.Parameters.AddWithValue("$updated_at", FormatUtc(recipe.UpdatedAtUtc));
            cmd.ExecuteNonQuery();
        });

        return true;
    }

    public bool TryDeleteRecipe(string id, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "recipe_id_required";
            return false;
        }

        var deleted = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM recipes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id.Trim());
            return cmd.ExecuteNonQuery();
        });

        if (deleted == 0)
        {
            error = "recipe_not_found";
            return false;
        }

        return true;
    }

    /// <summary>Seeds DB from setting when empty; otherwise loads DB recipes into setting.</summary>
    public void SyncRecipesWithSetting(MdkSetting setting)
    {
        var dbRecipes = ListRecipes();
        if (dbRecipes.Count == 0 && setting.Recipes.Count > 0)
        {
            foreach (var cfg in setting.Recipes)
            {
                TryUpsertRecipe(FromSettingRecipe(cfg), out _);
            }

            AppLog.Info($"Seeded {setting.Recipes.Count} recipe(s) into SQLite.");
            return;
        }

        if (dbRecipes.Count == 0)
        {
            return;
        }

        setting.Recipes = dbRecipes.Select(ToSettingRecipe).ToList();
        AppLog.Info($"Loaded {dbRecipes.Count} recipe(s) from SQLite.");
    }

    public void PersistRecipesFromSetting(MdkSetting setting)
    {
        foreach (var cfg in setting.Recipes)
        {
            TryUpsertRecipe(FromSettingRecipe(cfg), out _);
        }
    }

    // ── Teach points (点位) ────────────────────────────────────────────────

    public IReadOnlyList<TeachPointFileRecord> ListTeachFiles(string? platformId = null)
    {
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, platform_id, name, platform_kind, created_at, updated_at
                FROM teach_point_files
                """ + (string.IsNullOrWhiteSpace(platformId) ? "" : " WHERE platform_id = $platform_id") + """
                 ORDER BY platform_id, name COLLATE NOCASE
                """;
            if (!string.IsNullOrWhiteSpace(platformId))
            {
                cmd.Parameters.AddWithValue("$platform_id", platformId.Trim());
            }

            var list = new List<TeachPointFileRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadTeachFile(reader));
            }

            return list;
        });
    }

    public TeachPointFileSnapshot? GetTeachFileSnapshot(string platformId, string fileName = "default")
    {
        var file = FindTeachFile(platformId, fileName);
        if (file is null)
        {
            return null;
        }

        var points = ListTeachPoints(file.Id);
        return new TeachPointFileSnapshot
        {
            PlatformId = file.PlatformId,
            Kind = file.PlatformKind,
            FileName = file.Name,
            Points = points
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.PointId, StringComparer.OrdinalIgnoreCase)
                .Select(p => new TeachPointSnapshot(p.PointId, p.Name, p.Axes))
                .ToList(),
        };
    }

    public bool TrySaveTeachFileSnapshot(TeachPointFileSnapshot snapshot, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(snapshot.PlatformId))
        {
            error = "platform_id_required";
            return false;
        }

        var platformId = snapshot.PlatformId.Trim();
        var fileName = string.IsNullOrWhiteSpace(snapshot.FileName) ? "default" : snapshot.FileName.Trim();
        var now = DateTime.UtcNow;

        _db.Execute(conn =>
        {
            var fileId = FindOrCreateTeachFile(conn, platformId, fileName, snapshot.Kind, now);

            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM teach_points WHERE file_id = $file_id";
                del.Parameters.AddWithValue("$file_id", fileId);
                del.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var pt in snapshot.Points)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = """
                    INSERT INTO teach_points
                        (id, file_id, point_id, name, axes_json, sort_order, created_at, updated_at)
                    VALUES ($id, $file_id, $point_id, $name, $axes_json, $sort_order, $created_at, $updated_at)
                    """;
                ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                ins.Parameters.AddWithValue("$file_id", fileId);
                ins.Parameters.AddWithValue("$point_id", pt.PointId.Trim());
                ins.Parameters.AddWithValue("$name", pt.Name ?? string.Empty);
                ins.Parameters.AddWithValue("$axes_json", JsonSerializer.Serialize(pt.Axes, JsonOptions));
                ins.Parameters.AddWithValue("$sort_order", order++);
                ins.Parameters.AddWithValue("$created_at", FormatUtc(now));
                ins.Parameters.AddWithValue("$updated_at", FormatUtc(now));
                ins.ExecuteNonQuery();
            }

            using var touch = conn.CreateCommand();
            touch.CommandText = "UPDATE teach_point_files SET updated_at = $updated_at WHERE id = $id";
            touch.Parameters.AddWithValue("$updated_at", FormatUtc(now));
            touch.Parameters.AddWithValue("$id", fileId);
            touch.ExecuteNonQuery();
        });

        return true;
    }

    public bool TryUpsertTeachPoint(
        string platformId,
        string fileName,
        string pointId,
        string name,
        IReadOnlyDictionary<string, double> axes,
        string? platformKind,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(platformId) || string.IsNullOrWhiteSpace(pointId))
        {
            error = "platform_or_point_id_required";
            return false;
        }

        var now = DateTime.UtcNow;
        _db.Execute(conn =>
        {
            var fileId = FindOrCreateTeachFile(
                conn,
                platformId.Trim(),
                string.IsNullOrWhiteSpace(fileName) ? "default" : fileName.Trim(),
                platformKind,
                now);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO teach_points
                    (id, file_id, point_id, name, axes_json, sort_order, created_at, updated_at)
                VALUES ($id, $file_id, $point_id, $name, $axes_json, $sort_order, $created_at, $updated_at)
                ON CONFLICT(file_id, point_id) DO UPDATE SET
                    name = excluded.name,
                    axes_json = excluded.axes_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$file_id", fileId);
            cmd.Parameters.AddWithValue("$point_id", pointId.Trim());
            cmd.Parameters.AddWithValue("$name", name ?? string.Empty);
            cmd.Parameters.AddWithValue("$axes_json", JsonSerializer.Serialize(axes, JsonOptions));
            cmd.Parameters.AddWithValue("$sort_order", ResolveNextSortOrder(conn, fileId, pointId.Trim()));
            cmd.Parameters.AddWithValue("$created_at", FormatUtc(now));
            cmd.Parameters.AddWithValue("$updated_at", FormatUtc(now));
            cmd.ExecuteNonQuery();
        });

        return true;
    }

    public bool TryDeleteTeachPoint(string platformId, string fileName, string pointId, out string? error)
    {
        error = null;
        var file = FindTeachFile(platformId, fileName);
        if (file is null)
        {
            error = "teach_file_not_found";
            return false;
        }

        var deleted = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM teach_points WHERE file_id = $file_id AND point_id = $point_id";
            cmd.Parameters.AddWithValue("$file_id", file.Id);
            cmd.Parameters.AddWithValue("$point_id", pointId.Trim());
            return cmd.ExecuteNonQuery();
        });

        if (deleted == 0)
        {
            error = "teach_point_not_found";
            return false;
        }

        return true;
    }

    // ── Calibration (标定参数 / 结果) ─────────────────────────────────────

    public bool TryUpsertCalibParams(CalibParamsRecord record, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(record.TaskName))
        {
            error = "task_name_required";
            return false;
        }

        record.ProjectName = (record.ProjectName ?? "").Trim();
        record.TaskName = record.TaskName.Trim();
        record.Params ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        record.UpdatedAtUtc = DateTime.UtcNow;

        _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO calib_params (project_name, task_name, params_json, updated_at)
                VALUES ($project_name, $task_name, $params_json, $updated_at)
                ON CONFLICT(project_name, task_name) DO UPDATE SET
                    params_json = excluded.params_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$project_name", record.ProjectName);
            cmd.Parameters.AddWithValue("$task_name", record.TaskName);
            cmd.Parameters.AddWithValue("$params_json", JsonSerializer.Serialize(record.Params, JsonOptions));
            cmd.Parameters.AddWithValue("$updated_at", FormatUtc(record.UpdatedAtUtc));
            cmd.ExecuteNonQuery();
        });

        return true;
    }

    public bool TryGetCalibParams(string projectName, string taskName, out CalibParamsRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return false;
        }

        var project = (projectName ?? "").Trim();
        var task = taskName.Trim();
        record = _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT project_name, task_name, params_json, updated_at
                FROM calib_params
                WHERE project_name = $project_name AND task_name = $task_name
                """;
            cmd.Parameters.AddWithValue("$project_name", project);
            cmd.Parameters.AddWithValue("$task_name", task);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadCalibParams(reader) : null;
        });

        return record is not null;
    }

    public IReadOnlyList<CalibParamsRecord> ListCalibParams(string? projectName = null)
    {
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT project_name, task_name, params_json, updated_at
                FROM calib_params
                """ + (string.IsNullOrWhiteSpace(projectName) ? "" : " WHERE project_name = $project_name") + """
                 ORDER BY project_name, task_name COLLATE NOCASE
                """;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                cmd.Parameters.AddWithValue("$project_name", projectName.Trim());
            }

            var list = new List<CalibParamsRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadCalibParams(reader));
            }

            return list;
        });
    }

    public bool TryInsertCalibResult(CalibResultRecord record, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(record.TaskName))
        {
            error = "task_name_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.Id))
        {
            record.Id = Guid.NewGuid().ToString("N");
        }

        record.ProjectName = (record.ProjectName ?? "").Trim();
        record.TaskName = record.TaskName.Trim();
        record.Params ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        record.Results ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (record.CreatedAtUtc == default)
        {
            record.CreatedAtUtc = DateTime.UtcNow;
        }

        _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO calib_results
                    (id, project_name, task_name, params_json, results_json, ok, message, created_at)
                VALUES
                    ($id, $project_name, $task_name, $params_json, $results_json, $ok, $message, $created_at)
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$project_name", record.ProjectName);
            cmd.Parameters.AddWithValue("$task_name", record.TaskName);
            cmd.Parameters.AddWithValue("$params_json", JsonSerializer.Serialize(record.Params, JsonOptions));
            cmd.Parameters.AddWithValue("$results_json", JsonSerializer.Serialize(record.Results, JsonOptions));
            cmd.Parameters.AddWithValue("$ok", record.Ok ? 1 : 0);
            cmd.Parameters.AddWithValue("$message", record.Message ?? "");
            cmd.Parameters.AddWithValue("$created_at", FormatUtc(record.CreatedAtUtc));
            cmd.ExecuteNonQuery();
        });

        return true;
    }

    public IReadOnlyList<CalibResultRecord> ListCalibResults(string projectName, string? taskName = null)
    {
        var project = (projectName ?? "").Trim();
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, project_name, task_name, params_json, results_json, ok, message, created_at
                FROM calib_results
                WHERE project_name = $project_name
                """ + (string.IsNullOrWhiteSpace(taskName) ? "" : " AND task_name = $task_name") + """
                 ORDER BY created_at DESC
                """;
            cmd.Parameters.AddWithValue("$project_name", project);
            if (!string.IsNullOrWhiteSpace(taskName))
            {
                cmd.Parameters.AddWithValue("$task_name", taskName.Trim());
            }

            var list = new List<CalibResultRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadCalibResult(reader));
            }

            return list;
        });
    }

    public bool TryGetLatestCalibResult(string projectName, string taskName, out CalibResultRecord? record)
    {
        record = ListCalibResults(projectName, taskName).FirstOrDefault();
        return record is not null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _db.Dispose();
    }

    private TeachPointFileRecord? FindTeachFile(string platformId, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "default" : fileName.Trim();
        return ListTeachFiles(platformId.Trim())
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindOrCreateTeachFile(
        SqliteConnection conn,
        string platformId,
        string fileName,
        string? platformKind,
        DateTime now)
    {
        using (var find = conn.CreateCommand())
        {
            find.CommandText = """
                SELECT id FROM teach_point_files
                WHERE platform_id = $platform_id AND name = $name
                """;
            find.Parameters.AddWithValue("$platform_id", platformId);
            find.Parameters.AddWithValue("$name", fileName);
            var existing = find.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(existing))
            {
                if (!string.IsNullOrWhiteSpace(platformKind))
                {
                    using var upd = conn.CreateCommand();
                    upd.CommandText = "UPDATE teach_point_files SET platform_kind = $kind WHERE id = $id";
                    upd.Parameters.AddWithValue("$kind", platformKind.Trim());
                    upd.Parameters.AddWithValue("$id", existing);
                    upd.ExecuteNonQuery();
                }

                return existing;
            }
        }

        var id = Guid.NewGuid().ToString("N");
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO teach_point_files (id, platform_id, name, platform_kind, created_at, updated_at)
            VALUES ($id, $platform_id, $name, $platform_kind, $created_at, $updated_at)
            """;
        ins.Parameters.AddWithValue("$id", id);
        ins.Parameters.AddWithValue("$platform_id", platformId);
        ins.Parameters.AddWithValue("$name", fileName);
        ins.Parameters.AddWithValue("$platform_kind", (object?)platformKind ?? DBNull.Value);
        ins.Parameters.AddWithValue("$created_at", FormatUtc(now));
        ins.Parameters.AddWithValue("$updated_at", FormatUtc(now));
        ins.ExecuteNonQuery();
        return id;
    }

    private IReadOnlyList<TeachPointRecord> ListTeachPoints(string fileId)
    {
        return _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, file_id, point_id, name, axes_json, sort_order, created_at, updated_at
                FROM teach_points WHERE file_id = $file_id
                ORDER BY sort_order, point_id
                """;
            cmd.Parameters.AddWithValue("$file_id", fileId);
            var list = new List<TeachPointRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadTeachPoint(reader));
            }

            return list;
        });
    }

    private static int ResolveNextSortOrder(SqliteConnection conn, string fileId, string pointId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sort_order FROM teach_points
            WHERE file_id = $file_id AND point_id = $point_id
            """;
        cmd.Parameters.AddWithValue("$file_id", fileId);
        cmd.Parameters.AddWithValue("$point_id", pointId);
        var existing = cmd.ExecuteScalar();
        if (existing is not null && existing != DBNull.Value)
        {
            return Convert.ToInt32(existing);
        }

        cmd.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM teach_points WHERE file_id = $file_id";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static ProductionOrderRecord ReadOrder(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            Product = reader.GetString(1),
            Qty = reader.GetInt32(2),
            Status = reader.GetString(3),
            Progress = reader.GetDouble(4),
            RecipeId = reader.IsDBNull(5) ? null : reader.GetString(5),
            Priority = reader.GetInt32(6),
            Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
            Fields = ParseFieldsJson(reader.IsDBNull(8) ? "{}" : reader.GetString(8)),
            CreatedAtUtc = ParseUtc(reader.GetString(9)),
            UpdatedAtUtc = ParseUtc(reader.GetString(10)),
        };

    private static void BindOrder(SqliteCommand cmd, ProductionOrderRecord order)
    {
        cmd.Parameters.AddWithValue("$id", order.Id);
        cmd.Parameters.AddWithValue("$product", order.Product ?? string.Empty);
        cmd.Parameters.AddWithValue("$qty", order.Qty);
        cmd.Parameters.AddWithValue("$status", order.Status ?? "pending");
        cmd.Parameters.AddWithValue("$progress", order.Progress);
        cmd.Parameters.AddWithValue("$recipe_id", (object?)order.RecipeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", order.Priority);
        cmd.Parameters.AddWithValue("$notes", (object?)order.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "$fields_json",
            JsonSerializer.Serialize(order.Fields ?? new Dictionary<string, string>(), JsonOptions));
        cmd.Parameters.AddWithValue("$created_at", FormatUtc(order.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$updated_at", FormatUtc(order.UpdatedAtUtc));
    }

    private static Dictionary<string, string> ParseFieldsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return dict is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static RecipeRecord ReadRecipe(SqliteDataReader reader)
    {
        var varsJson = reader.GetString(3);
        var vars = JsonSerializer.Deserialize<Dictionary<string, object?>>(varsJson, JsonOptions)
                   ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new RecipeRecord
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Vars = vars,
            CreatedAtUtc = ParseUtc(reader.GetString(4)),
            UpdatedAtUtc = ParseUtc(reader.GetString(5)),
        };
    }

    private static TeachPointFileRecord ReadTeachFile(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            PlatformId = reader.GetString(1),
            Name = reader.GetString(2),
            PlatformKind = reader.IsDBNull(3) ? null : reader.GetString(3),
            CreatedAtUtc = ParseUtc(reader.GetString(4)),
            UpdatedAtUtc = ParseUtc(reader.GetString(5)),
        };

    private static TeachPointRecord ReadTeachPoint(SqliteDataReader reader)
    {
        var axesJson = reader.GetString(4);
        var axes = JsonSerializer.Deserialize<Dictionary<string, double>>(axesJson, JsonOptions)
                   ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        return new TeachPointRecord
        {
            Id = reader.GetString(0),
            FileId = reader.GetString(1),
            PointId = reader.GetString(2),
            Name = reader.GetString(3),
            Axes = axes,
            SortOrder = reader.GetInt32(5),
            CreatedAtUtc = ParseUtc(reader.GetString(6)),
            UpdatedAtUtc = ParseUtc(reader.GetString(7)),
        };
    }

    private static CalibParamsRecord ReadCalibParams(SqliteDataReader reader) =>
        new()
        {
            ProjectName = reader.GetString(0),
            TaskName = reader.GetString(1),
            Params = ParseFieldsJson(reader.GetString(2)),
            UpdatedAtUtc = ParseUtc(reader.GetString(3)),
        };

    private static CalibResultRecord ReadCalibResult(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            ProjectName = reader.GetString(1),
            TaskName = reader.GetString(2),
            Params = ParseFieldsJson(reader.GetString(3)),
            Results = ParseFieldsJson(reader.GetString(4)),
            Ok = reader.GetInt32(5) != 0,
            Message = reader.IsDBNull(6) ? "" : reader.GetString(6),
            CreatedAtUtc = ParseUtc(reader.GetString(7)),
        };

    private static RecipeRecord FromSettingRecipe(MdkSetting.RecipeConfig cfg) =>
        new()
        {
            Id = cfg.Id,
            Name = cfg.Name,
            Description = cfg.Description,
            Vars = new Dictionary<string, object?>(cfg.Vars, StringComparer.OrdinalIgnoreCase),
        };

    private static MdkSetting.RecipeConfig ToSettingRecipe(RecipeRecord record) =>
        new()
        {
            Id = record.Id,
            Name = record.Name,
            Description = record.Description,
            Vars = new Dictionary<string, object?>(record.Vars, StringComparer.OrdinalIgnoreCase),
        };

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("O");

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
