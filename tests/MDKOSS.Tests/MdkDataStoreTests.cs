using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.Tests;

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
        };

        Assert.True(store.TryUpsertOrder(order, out var error), error);
        Assert.True(store.TryGetOrder("ORD-001", out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal("Widget-A", loaded!.Product);
        Assert.Equal(100, loaded.Qty);
        Assert.Equal("default", loaded.RecipeId);
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

        var setting = new MdkSetting { DatabasePath = dbPath };
        using var rt = new MdkRuntime(setting);
        rt.Initialize();

        var listJson = rt.Vars.Get<string>(MdkDataStore.OrderListVarKey);
        Assert.NotNull(listJson);
        Assert.Contains("ORD-BOOT", listJson);
    }
}
