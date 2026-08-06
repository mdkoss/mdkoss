using MDKOSS.Core;
using MDKOSS.Gui;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace MDKOSS.Tests.Config;

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

    [Fact]
    public void ComponentConfigForm_modified_rows_can_be_saved_to_setting_json()
    {
        var settingPath = Path.Combine(Path.GetTempPath(), $"mdkoss-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigFormHelpersForTests.SaveMinimalSetting(settingPath);

            var ex = Record.Exception(() => RunOnStaThread(() =>
            {
                using var form = new ComponentConfigForm(settingPath);

                GetField<TextBox>(form, "_projectNameBox").Text = "Updated Project";
                GetField<NumericUpDown>(form, "_cycleMsBox").Value = 250;
                GetField<TextBox>(form, "_monitoringPrefixBox").Text = "http://127.0.0.1:5082/";

                var drivers = GetBindingSourceList<ComponentConfigForm.DriverRow>(form, "_driversBinding");
                drivers[0].Id = "drv-updated";
                drivers[0].Type = "sim";
                drivers[0].Enabled = false;
                drivers[0].Parameters = "connect=false; port=COM9";

                var devices = GetBindingSourceList<ComponentConfigForm.DeviceRow>(form, "_devicesBinding");
                devices[0].Name = "Updated GPIO";
                devices[0].DriverId = "drv-updated";
                devices[0].Parameters = "driverIds=drv-updated; custom=kept";

                var ioLabels = GetBindingSourceList<ComponentConfigForm.IoLabelRow>(form, "_ioLabelsBinding");
                foreach (var label in ioLabels)
                {
                    label.DriverId = "drv-updated";
                }

                var readyLabel = ioLabels.Single(r => r.Alias == "ready");
                readyLabel.Address = "7";
                readyLabel.Description = "ready signal";

                var tasks = GetBindingSourceList<ComponentConfigForm.TaskRow>(form, "_tasksBinding");
                tasks[0].DriverId = "drv-updated";
                tasks[0].IntervalMs = 500;
                tasks[0].Parameters = "varPrefix=driver.updated";

                var vars = GetBindingSourceList<ComponentConfigForm.VarRow>(form, "_varsBinding");
                vars.Add(new ComponentConfigForm.VarRow { Key = "threshold", ValueJson = "42" });
                vars.Add(new ComponentConfigForm.VarRow { Key = "flags", ValueJson = "{\"enabled\":true}" });

                var setting = InvokeBuildSettingFromRows(form);
                InvokeSaveSetting(settingPath, setting);
            }));

            Assert.Null(ex);

            var saved = MdkSetting.Load(settingPath);
            Assert.Equal("Updated Project", saved.ProjectName);
            Assert.Equal(250, saved.CycleMs);
            Assert.Equal("http://127.0.0.1:5082/", saved.MonitoringPrefix);

            var driver = Assert.Single(saved.Drivers);
            Assert.Equal("drv-updated", driver.Id);
            Assert.False(driver.Enabled);
            Assert.Equal("false", driver.Parameters["connect"]);
            Assert.Equal("COM9", driver.Parameters["port"]);

            var device = Assert.Single(saved.Devices);
            Assert.Equal("Updated GPIO", device.Name);
            Assert.Equal("drv-updated", device.DriverId);
            Assert.Equal("kept", device.Parameters["custom"]);
            // Same-driver points prefer short form address|label (no desc.* keys).
            Assert.Equal("7|ready signal", device.Parameters["in.ready"]);
            Assert.False(device.Parameters.ContainsKey("desc.ready"));
            Assert.Equal("1", device.Parameters["out.start"]);

            var task = Assert.Single(saved.Tasks);
            Assert.Equal("drv-updated", task.DriverId);
            Assert.Equal(500, task.IntervalMs);
            Assert.Equal("driver.updated", task.Parameters["varPrefix"]);

            using var document = JsonDocument.Parse(File.ReadAllText(settingPath));
            var root = document.RootElement;
            Assert.Equal("Updated Project", root.GetProperty(nameof(MdkSetting.ProjectName)).GetString());
            Assert.Equal(42, root.GetProperty(nameof(MdkSetting.Vars)).GetProperty("threshold").GetInt32());
            Assert.True(root.GetProperty(nameof(MdkSetting.Vars)).GetProperty("flags").GetProperty("enabled").GetBoolean());
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

    private static T GetField<T>(ComponentConfigForm form, string fieldName)
        where T : class
    {
        return (T)(typeof(ComponentConfigForm)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(form) ?? throw new MissingFieldException(nameof(ComponentConfigForm), fieldName));
    }

    private static List<T> GetBindingSourceList<T>(ComponentConfigForm form, string fieldName)
    {
        var source = GetField<BindingSource>(form, fieldName);
        return (List<T>)source.DataSource;
    }

    private static MdkSetting InvokeBuildSettingFromRows(ComponentConfigForm form)
    {
        return (MdkSetting)(typeof(ComponentConfigForm)
            .GetMethod("BuildSettingFromRows", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(form, null) ?? throw new MissingMethodException(nameof(ComponentConfigForm), "BuildSettingFromRows"));
    }

    private static void InvokeSaveSetting(string settingPath, MdkSetting setting)
    {
        var helperType = typeof(ComponentConfigForm).Assembly.GetType("MDKOSS.Gui.ConfigFormHelpers", throwOnError: true)!;
        var method = helperType.GetMethod("SaveSetting", BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMethodException("ConfigFormHelpers", "SaveSetting");
        method.Invoke(null, [settingPath, setting]);
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
