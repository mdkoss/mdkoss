using MDKOSS.Core;
using MDKOSS.Gui;

namespace MDKOSS.Tests;

public sealed class WinFormsComponentConfigFormTests
{
    [Fact]
    public void ComponentConfigForm_constructor_does_not_throw_before_layout()
    {
        var settingPath = Path.Combine(Path.GetTempPath(), $"mdkoss-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigFormHelpersForTests.SaveMinimalSetting(settingPath);

            var ex = Record.Exception(() => RunOnStaThread(() =>
            {
                using var form = new ComponentConfigForm(settingPath);
                Assert.Equal("Component Config Manager", form.Text);
            }));

            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(settingPath))
            {
                File.Delete(settingPath);
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static class ConfigFormHelpersForTests
    {
        public static void SaveMinimalSetting(string path)
        {
            var setting = new MdkSetting
            {
                ProjectName = "WinForms Test",
                Drivers = [new MdkSetting.DriverConfig { Id = "drv-main", Type = "sim", Enabled = true }],
                Devices =
                [
                    new MdkSetting.DeviceConfig
                    {
                        Id = "gpio-main",
                        Name = "GPIO",
                        Type = "gpio",
                        DriverId = "drv-main",
                        Enabled = true,
                        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["in.ready"] = "drv-main:0",
                            ["out.start"] = "drv-main:1"
                        }
                    }
                ],
                Tasks = [new MdkSetting.TaskConfig { Name = "poll", Type = "pollDriver", DriverId = "drv-main", IntervalMs = 100 }]
            };

            var json = System.Text.Json.JsonSerializer.Serialize(setting, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
