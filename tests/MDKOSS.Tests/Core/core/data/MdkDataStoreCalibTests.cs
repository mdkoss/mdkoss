using MDKOSS.Core.Data;

namespace MDKOSS.Tests.Core;

public sealed class MdkDataStoreCalibTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"mdk-calib-db-{Guid.NewGuid():N}.db");

    [Fact]
    public void CalibParams_upsert_get_and_overwrite()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);

        Assert.False(store.TryGetCalibParams("proj-a", "task-x", out _));
        Assert.False(store.TryUpsertCalibParams(new CalibParamsRecord { ProjectName = "proj-a" }, out var missingName));
        Assert.Equal("task_name_required", missingName);

        Assert.True(store.TryUpsertCalibParams(new CalibParamsRecord
        {
            ProjectName = "proj-a",
            TaskName = "task-x",
            Params = { ["expectedPos"] = "10", ["axisLetter"] = "X" },
        }, out var error), error);

        Assert.True(store.TryGetCalibParams("proj-a", "task-x", out var loaded));
        Assert.Equal("10", loaded!.Params["expectedPos"]);
        Assert.Equal("X", loaded.Params["axisLetter"]);

        Assert.True(store.TryUpsertCalibParams(new CalibParamsRecord
        {
            ProjectName = "proj-a",
            TaskName = "task-x",
            Params = { ["expectedPos"] = "12" },
        }, out error), error);

        Assert.True(store.TryGetCalibParams("proj-a", "task-x", out loaded));
        Assert.Equal("12", loaded!.Params["expectedPos"]);
        Assert.False(loaded.Params.ContainsKey("axisLetter"));

        var listed = store.ListCalibParams("proj-a");
        Assert.Single(listed);
        Assert.Empty(store.ListCalibParams("other"));
    }

    [Fact]
    public void CalibResults_history_and_latest()
    {
        var dbPath = CreateTempDbPath();
        using var store = new MdkDataStore(dbPath);

        Assert.False(store.TryInsertCalibResult(new CalibResultRecord { ProjectName = "p" }, out var missing));
        Assert.Equal("task_name_required", missing);

        Assert.True(store.TryInsertCalibResult(new CalibResultRecord
        {
            ProjectName = "p",
            TaskName = "t1",
            Params = { ["expectedPos"] = "1" },
            Results = { ["ok"] = "false" },
            Ok = false,
            Message = "first",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }, out var error), error);

        Thread.Sleep(15);
        Assert.True(store.TryInsertCalibResult(new CalibResultRecord
        {
            ProjectName = "p",
            TaskName = "t1",
            Params = { ["expectedPos"] = "2" },
            Results = { ["ok"] = "true", ["offset"] = "0.1" },
            Ok = true,
            Message = "second",
        }, out error), error);

        Assert.True(store.TryInsertCalibResult(new CalibResultRecord
        {
            ProjectName = "p",
            TaskName = "t2",
            Results = { ["ok"] = "true" },
            Ok = true,
            Message = "other",
        }, out error), error);

        var history = store.ListCalibResults("p", "t1");
        Assert.Equal(2, history.Count);
        Assert.Equal("second", history[0].Message);
        Assert.True(history[0].Ok);
        Assert.Equal("0.1", history[0].Results["offset"]);
        Assert.Equal("first", history[1].Message);

        Assert.True(store.TryGetLatestCalibResult("p", "t1", out var latest));
        Assert.Equal("second", latest!.Message);
        Assert.False(store.TryGetLatestCalibResult("p", "missing", out _));
        Assert.Equal(3, store.ListCalibResults("p").Count);
    }

    [Fact]
    public void Schema_v5_creates_calib_tables_on_existing_db()
    {
        var dbPath = CreateTempDbPath();
        using (var first = new MdkDataStore(dbPath))
        {
            Assert.True(first.TryUpsertOrder(new ProductionOrderRecord
            {
                Id = "ORD-KEEP",
                Product = "keep",
                Qty = 1,
            }, out var error), error);
        }

        using var store = new MdkDataStore(dbPath);
        Assert.True(store.TryGetOrder("ORD-KEEP", out _));
        Assert.True(store.TryUpsertCalibParams(new CalibParamsRecord
        {
            ProjectName = "p",
            TaskName = "t",
            Params = { ["a"] = "1" },
        }, out var calibError), calibError);
        Assert.True(store.TryGetCalibParams("p", "t", out var record));
        Assert.Equal("1", record!.Params["a"]);
    }
}
