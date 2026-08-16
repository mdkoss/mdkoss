using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

[Collection("RecipeTests")]
public sealed class MdkRecipeTests
{
    [Fact]
    public void Bootstrap_applies_active_recipe_vars()
    {
        var setting = BuildSettingWithRecipes();
        using var rt = new MdkRuntime(setting);
        rt.Initialize();

        Assert.Equal("default", rt.RecipeManager.ActiveRecipeId);
        Assert.Equal("AUTO", rt.Vars.Get<string>("machine.mode"));
        Assert.Equal("lamp:green", rt.Vars.Get<string>("task.operation.command"));
        Assert.Equal("default", rt.Vars.Get<string>(MdkRecipeManager.ActiveIdVarKey));
        Assert.Equal("默认配方", rt.Vars.Get<string>(MdkRecipeManager.ActiveNameVarKey));
        Assert.True(rt.Vars.Get<bool>("machine.ready"));
    }

    [Fact]
    public void TryApplyRecipe_switches_runtime_vars()
    {
        var setting = BuildSettingWithRecipes();
        using var rt = new MdkRuntime(setting);
        rt.Initialize();

        Assert.True(rt.TryApplyRecipe("manual", out var error), error);
        Assert.Equal("manual", rt.RecipeManager.ActiveRecipeId);
        Assert.Equal("MANUAL", rt.Vars.Get<string>("machine.mode"));
        Assert.Equal("lamp:yellow", rt.Vars.Get<string>("task.operation.command"));
    }

    [Fact]
    public void TryApplyRecipe_pushes_all_recipe_vars_including_extra_seed_keys()
    {
        var setting = BuildSettingWithRecipes();
        setting.Vars["machine.ready"] = false;
        setting.Recipes.First(r => r.Id == "manual").Vars["machine.ready"] = true;

        using var rt = new MdkRuntime(setting);
        rt.Initialize();

        Assert.True(rt.TryApplyRecipe("manual", out var error), error);
        Assert.Equal("MANUAL", rt.Vars.Get<string>("machine.mode"));
        Assert.Equal("lamp:yellow", rt.Vars.Get<string>("task.operation.command"));
        Assert.True(rt.Vars.Get<bool>("machine.ready"));
    }

    [Fact]
    public void TryApplyRecipe_updates_task_platform_and_device_parameters()
    {
        var setting = BuildSettingWithRecipes();
        setting.Tasks =
        [
            new MdkSetting.TaskConfig
            {
                Name = "bond-cycle",
                Type = "bond",
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dwellTicks"] = "1",
                },
            },
        ];
        setting.Platforms =
        [
            new MdkSetting.DeviceConfig
            {
                Id = "head-bond",
                Type = "xyzu",
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["note"] = "base",
                },
            },
        ];
        setting.Devices =
        [
            new MdkSetting.DeviceConfig
            {
                Id = "tray-wafer",
                Type = "tray",
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pickZ"] = "-1",
                },
            },
        ];
        setting.RecipeVarKeys.Add("task.bond-cycle.dwellTicks");
        setting.RecipeVarKeys.Add("platform.head-bond.note");
        setting.RecipeVarKeys.Add("device.tray-wafer.pickZ");
        setting.Recipes.First(r => r.Id == "manual").Vars["task.bond-cycle.dwellTicks"] = "9";
        setting.Recipes.First(r => r.Id == "manual").Vars["platform.head-bond.note"] = "manual-head";
        setting.Recipes.First(r => r.Id == "manual").Vars["device.tray-wafer.pickZ"] = "-12";

        var vars = new MVarStore();
        foreach (var kv in setting.Vars)
        {
            vars.Set(kv.Key, kv.Value);
        }

        var mgr = new MdkRecipeManager(setting, vars);
        Assert.True(mgr.TryApplyRecipe("manual", out var error), error);
        Assert.Equal("9", setting.Tasks[0].Parameters["dwellTicks"]);
        Assert.Equal("manual-head", setting.Platforms[0].Parameters["note"]);
        Assert.Equal("-12", setting.Devices[0].Parameters["pickZ"]);
        Assert.Equal("9", vars.Get<string>("task.bond-cycle.dwellTicks"));
        Assert.Equal("manual-head", vars.Get<string>("platform.head-bond.note"));
        Assert.Equal("-12", vars.Get<string>("device.tray-wafer.pickZ"));
    }

    [Fact]
    public void TrySplitOwnerParamKey_parses_structured_recipe_keys()
    {
        Assert.True(MdkRecipeManager.TrySplitOwnerParamKey(
            "task.bond-cycle.dwellTicks", out var kind, out var owner, out var param));
        Assert.Equal("task", kind);
        Assert.Equal("bond-cycle", owner);
        Assert.Equal("dwellTicks", param);

        Assert.True(MdkRecipeManager.TrySplitOwnerParamKey(
            "platform.head-bond.note", out kind, out owner, out param));
        Assert.Equal("platform", kind);
        Assert.Equal("head-bond", owner);
        Assert.Equal("note", param);

        Assert.False(MdkRecipeManager.TrySplitOwnerParamKey("machine.mode", out _, out _, out _));
    }

    [Fact]
    public void TryCaptureFromRuntime_updates_existing_recipe()
    {
        var setting = BuildSettingWithRecipes();
        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Vars.Set("machine.mode", "SERVICE");
        rt.Vars.Set("task.operation.command", "lamp:red");

        Assert.True(rt.RecipeManager.TryCaptureFromRuntime("default", "默认配方", out var error), error);
        var recipe = rt.RecipeManager.Recipes.First(r => r.Id == "default");
        Assert.Equal("SERVICE", recipe.Vars["machine.mode"]?.ToString());
        Assert.Equal("lamp:red", recipe.Vars["task.operation.command"]?.ToString());
    }

    [Fact]
    public void Load_sample_setting_includes_recipes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        var setting = MdkSetting.Load(path);
        Assert.Equal("default", setting.ActiveRecipeId);
        Assert.NotEmpty(setting.Recipes);
        Assert.Contains(setting.Recipes, r => string.Equals(r.Id, "default", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setting.RecipeVarKeys, k => k == "machine.mode");
    }

    [Fact]
    public void Save_round_trips_recipe_section()
    {
        var setting = BuildSettingWithRecipes();
        var path = Path.Combine(Path.GetTempPath(), $"mdk-recipe-{Guid.NewGuid():N}.json");
        try
        {
            setting.Save(path);
            var loaded = MdkSetting.Load(path);
            Assert.Equal(setting.ActiveRecipeId, loaded.ActiveRecipeId);
            Assert.Equal(setting.Recipes.Count, loaded.Recipes.Count);
            Assert.Equal(
                setting.Recipes[0].Vars["machine.mode"]?.ToString(),
                loaded.Recipes[0].Vars["machine.mode"]?.ToString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static MdkSetting BuildSettingWithRecipes() =>
        new()
        {
            ProjectName = "recipe-test",
            DatabasePath = Path.Combine(Path.GetTempPath(), $"mdk-recipe-test-{Guid.NewGuid():N}.db"),
            RecipeVarKeys = ["machine.mode", "task.operation.command"],
            ActiveRecipeId = "default",
            Recipes =
            [
                new MdkSetting.RecipeConfig
                {
                    Id = "default",
                    Name = "默认配方",
                    Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["machine.mode"] = "AUTO",
                        ["task.operation.command"] = "lamp:green",
                    },
                },
                new MdkSetting.RecipeConfig
                {
                    Id = "manual",
                    Name = "手动配方",
                    Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["machine.mode"] = "MANUAL",
                        ["task.operation.command"] = "lamp:yellow",
                    },
                },
            ],
            Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["machine.mode"] = "AUTO",
                ["machine.ready"] = true,
                ["task.operation.command"] = "lamp:green",
            },
        };
}
