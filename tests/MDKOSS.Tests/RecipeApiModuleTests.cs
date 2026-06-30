using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Tests;

public sealed class RecipeApiModuleTests
{
    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Recipe_api_lists_and_applies_recipes()
    {
        var port = GetFreeLoopbackPort();
        var setting = new MdkSetting
        {
            ProjectName = "recipe-api",
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            RecipeVarKeys = ["machine.mode", "task.operation.command"],
            ActiveRecipeId = "default",
            Recipes =
            [
                new MdkSetting.RecipeConfig
                {
                    Id = "default",
                    Name = "默认",
                    Vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["machine.mode"] = "AUTO",
                        ["task.operation.command"] = "lamp:green",
                    },
                },
                new MdkSetting.RecipeConfig
                {
                    Id = "manual",
                    Name = "手动",
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
                ["task.operation.command"] = "lamp:green",
            },
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        rt.Start();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            var listJson = await client.GetStringAsync("/api/recipe");
            using var listDoc = JsonDocument.Parse(listJson);
            Assert.True(listDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("default", listDoc.RootElement.GetProperty("activeRecipeId").GetString());
            Assert.Equal(2, listDoc.RootElement.GetProperty("recipes").GetArrayLength());

            var applyRes = await client.PostAsync("/api/recipe/apply?id=manual", null);
            applyRes.EnsureSuccessStatusCode();
            using var applyDoc = JsonDocument.Parse(await applyRes.Content.ReadAsStringAsync());
            Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("manual", applyDoc.RootElement.GetProperty("activeRecipeId").GetString());

            Assert.Equal("MANUAL", rt.Vars.Get<string>("machine.mode"));
            Assert.Equal("lamp:yellow", rt.Vars.Get<string>("task.operation.command"));
        }
        finally
        {
            await rt.StopAsync();
        }
    }
}
