using MDKOSS.Core;

namespace MDKOSS.Tests;

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
        Assert.Equal(2, setting.Recipes.Count);
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
