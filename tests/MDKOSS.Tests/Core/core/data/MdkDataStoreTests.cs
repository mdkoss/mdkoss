using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Tests.Core;

public sealed class MdkDataStoreTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"mdk-db-{Guid.NewGuid():N}.db");

    [Fact]
    public void UpsertOrder_round_trips()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        var order = new ProductionOrderRecord
        {
            Id = "ORD-001",
            Product = "Widget-A",
            Qty = 100,
            Status = "pending",
            Progress = 0,
            RecipeId = "default",
            Priority = 1,
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lot"] = "L-100",
                ["line"] = "A",
            },
        };

        Assert.True(store.TryUpsertOrder(order, out var error), error);
        Assert.True(store.TryGetOrder("ORD-001", out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal("Widget-A", loaded!.Product);
        Assert.Equal(100, loaded.Qty);
        Assert.Equal("default", loaded.RecipeId);
        Assert.Equal("L-100", loaded.Fields["lot"]);
        Assert.Equal("A", loaded.Fields["line"]);
    }

    [Fact]
    public void SerializeOrdersForVar_uses_camel_case_and_fields()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        Assert.True(store.TryUpsertOrder(new ProductionOrderRecord
        {
            Id = "ORD-JSON",
            Product = "P",
            Qty = 2,
            Fields = { ["customer"] = "ACME" },
        }, out var error), error);

        var json = store.SerializeOrdersForVar();
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"product\"", json);
        Assert.DoesNotContain("\"Id\"", json);
        Assert.Contains("\"customer\":\"ACME\"", json.Replace(" ", ""));
    }

    [Fact]
    public void UpsertOrder_absorbs_extension_json_properties()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        var order = System.Text.Json.JsonSerializer.Deserialize<ProductionOrderRecord>(
            """{"id":"ORD-EXT","product":"X","qty":1,"lotNo":"LOT-9","bay":"B2"}""",
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            })!;

        Assert.True(store.TryUpsertOrder(order, out var error), error);
        Assert.True(store.TryGetOrder("ORD-EXT", out var loaded));
        Assert.Equal("LOT-9", loaded!.Fields["lotNo"]);
        Assert.Equal("B2", loaded.Fields["bay"]);
    }

    [Fact]
    public void SyncOrdersFromSettingVars_seeds_empty_db()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        var vars = new Dictionary<string, object?>
        {
            [MdkDataStore.OrderListVarKey] =
                """[{"id":"ORD-SEED","product":"Seed","qty":3,"status":"pending","fields":{"lot":"S1"}}]""",
        };

        Assert.Equal(1, store.SyncOrdersFromSettingVars(vars));
        Assert.True(store.TryGetOrder("ORD-SEED", out var loaded));
        Assert.Equal("Seed", loaded!.Product);
        Assert.Equal("S1", loaded.Fields["lot"]);
        Assert.Equal(0, store.SyncOrdersFromSettingVars(vars));
    }

    [Fact]
    public void ConfigStore_production_orders_custom_fields_round_trip()
    {
        var dbPath = CreateTempDbPath();
        using (var _ = new MdkDataStore(dbPath)) { }

        using var cfg = new MdkConfigStore(dbPath);
        var pk = cfg.UpsertTableRow("production_orders", new Dictionary<string, string>
        {
            ["id"] = "ORD-CFG",
            ["product"] = "CfgPart",
            ["qty"] = "5",
            ["status"] = "pending",
            ["progress"] = "0",
            ["priority"] = "1",
            ["lot"] = "CFG-LOT",
            ["operator"] = "alice",
        });
        Assert.Equal("ORD-CFG", pk);

        var snap = cfg.QueryTable("production_orders");
        var row = Assert.Single(snap.Rows);
        Assert.Equal("CfgPart", row["product"]);
        Assert.Equal("CFG-LOT", row["lot"]);
        Assert.Equal("alice", row["operator"]);
        Assert.False(row.ContainsKey("fields_json"));
    }

    [Fact]
    public void Runtime_bootstrap_injects_order_list_var()
    {
        var dbPath = CreateTempDbPath();
        using (var seed = new MdkDataStore(dbPath))
        {
            seed.TryUpsertOrder(new ProductionOrderRecord
            {
                Id = "ORD-BOOT",
                Product = "Test",
                Qty = 1,
                Status = "running",
                Progress = 50,
            }, out _);
        }

        var setting = new MdkSetting
        {
            DatabasePath = dbPath,
            Vars =
            {
                // Would previously hide SQLite orders after BootstrapVars.
                [MdkDataStore.OrderListVarKey] = "[]",
            },
        };
        using var rt = new MdkRuntime(setting);
        rt.Initialize();

        var listJson = rt.Vars.Get<string>(MdkDataStore.OrderListVarKey);
        Assert.NotNull(listJson);
        Assert.Contains("ORD-BOOT", listJson);
        Assert.Contains("\"id\"", listJson);
    }

    [Fact]
    public void SyncRecipes_seeds_from_setting_when_db_empty()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        var setting = new MdkSetting
        {
            Recipes =
            [
                new MdkSetting.RecipeConfig
                {
                    Id = "r1",
                    Name = "配方一",
                    Vars = new Dictionary<string, object?> { ["machine.mode"] = "AUTO" },
                },
            ],
        };

        store.SyncRecipesWithSetting(setting);
        var recipes = store.ListRecipes();
        Assert.Single(recipes);
        Assert.Equal("r1", recipes[0].Id);
        Assert.Equal("配方一", recipes[0].Name);
    }

    [Fact]
    public void TeachPoint_upsert_and_load_snapshot()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);
        Assert.True(store.TryUpsertTeachPoint(
            "dev-platform-xyz",
            "default",
            "P0",
            "Home",
            new Dictionary<string, double> { ["X"] = 0, ["Y"] = 0, ["Z"] = 10 },
            "xyz",
            out var error), error);

        var snapshot = store.GetTeachFileSnapshot("dev-platform-xyz");
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Points);
        Assert.Equal("P0", snapshot.Points[0].PointId);
        Assert.Equal(10, snapshot.Points[0].Axes["Z"]);
    }
}
