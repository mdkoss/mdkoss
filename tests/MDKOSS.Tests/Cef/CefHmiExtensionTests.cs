using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MDKOSS.Cef.Extensions;
using MDKOSS.Core;
using MDKOSS.Extensions;

namespace MDKOSS.Tests.Cef;

public sealed class CefHmiExtensionTests
{
    public CefHmiExtensionTests()
    {
        TestPluginBootstrap.EnsureRegistered();
        MdkExtensionHost.Register(new CefHmiExtension());
    }

    private static int FreePort()
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
    public void Catalog_includes_first_batch_widgets()
    {
        var types = HmiWidgetCatalog.All.Select(w => w.Type).ToArray();
        Assert.Contains("label", types);
        Assert.Contains("value", types);
        Assert.Contains("lamp", types);
        Assert.Contains("button", types);
        Assert.Contains("progress", types);
        Assert.Contains("status", types);
        Assert.True(HmiWidgetCatalog.IsKnown("value"));
        Assert.False(HmiWidgetCatalog.IsKnown("__no_such_widget__"));
        Assert.Equal("hmi-builtin", HmiWidgetCatalog.Find("label")?.Package);
        Assert.Equal("/api/hmi/widget/label.js", HmiWidgetCatalog.Find("label")?.Script);
    }

    [Fact]
    public void Discover_folder_registers_extra_widget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdk-hmi-w-{Guid.NewGuid():N}");
        var pack = Path.Combine(root, "xtest_gauge");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "widget.json"),
            """{"type":"xtest_gauge","displayName":"表盘","category":"显示","defaultW":80,"defaultH":80,"props":[{"key":"var","label":"变量","kind":"var","default":"x"}]}""");
        File.WriteAllText(Path.Combine(pack, "widget.js"), "MdkHmi.register('xtest_gauge',{create(){}});");
        try
        {
            HmiWidgetRegistry.DiscoverFolder(root, "file-test");
            Assert.True(HmiWidgetCatalog.IsKnown("xtest_gauge"));
            var widget = HmiWidgetCatalog.CreateInstance("xtest_gauge", 1, 2, "w-g");
            Assert.Equal("xtest_gauge", widget.Type);
            Assert.Equal(80, widget.W);
            Assert.Equal("x", HmiProps.GetString(widget.Props, "var"));
            Assert.True(HmiWidgetRegistry.TryGetAsset("xtest_gauge.js", out var contentType, out var body));
            Assert.Contains("javascript", contentType, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("xtest_gauge", body, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Default_layout_round_trips_json()
    {
        var original = HmiLayout.CreateDefault();
        var json = HmiLayoutStore.Serialize(original);
        var parsed = HmiLayoutStore.Parse(json);
        Assert.NotNull(parsed);
        Assert.Equal(original.Widgets.Count, parsed!.Widgets.Count);
        Assert.Contains(parsed.Widgets, w => w.Type == "value" && HmiProps.GetString(w.Props, "var") == "task.operation.state");
        Assert.Contains(parsed.Widgets, w => w.Type == "button" && HmiProps.GetString(w.Props, "url") == "/api/task/start");
    }

    [Fact]
    public void CreateInstance_fills_catalog_defaults()
    {
        var widget = HmiWidgetCatalog.CreateInstance("progress", 10, 20, "w-p1");
        Assert.Equal("progress", widget.Type);
        Assert.Equal("w-p1", widget.Id);
        Assert.Equal(10, widget.X);
        Assert.Equal(100, HmiProps.GetNumber(widget.Props, "max"));
    }

    [Fact]
    public async Task Runtime_serves_layout_api_and_pages()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-hmi-{Guid.NewGuid():N}.db");
        var settingDir = Path.Combine(Path.GetTempPath(), $"mdk-hmi-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(settingDir);
        var settingPath = Path.Combine(settingDir, "sample.setting.json");
        var port = FreePort();
        var setting = new MdkSetting
        {
            ProjectName = "hmi-test",
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            DatabasePath = db,
        };

        try
        {
            using var rt = new MdkRuntime(setting, settingPath);
            rt.Initialize();
            rt.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var prefix = $"http://127.0.0.1:{port}";

            using (var widgets = await http.GetAsync(prefix + "/api/hmi/widgets"))
            {
                widgets.EnsureSuccessStatusCode();
                var json = await widgets.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("widgets").GetArrayLength() >= 6);
                Assert.Contains(doc.RootElement.GetProperty("widgets").EnumerateArray(),
                    w => w.GetProperty("type").GetString() == "label"
                         && w.GetProperty("script").GetString() == "/api/hmi/widget/label.js");
            }

            using (var script = await http.GetAsync(prefix + "/api/hmi/widget/label.js"))
            {
                script.EnsureSuccessStatusCode();
                var js = await script.Content.ReadAsStringAsync();
                Assert.Contains("register", js, StringComparison.Ordinal);
            }

            using (var get = await http.GetAsync(prefix + "/api/hmi/layout"))
            {
                get.EnsureSuccessStatusCode();
                var json = await get.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
                Assert.Contains("hmi.layout.json", doc.RootElement.GetProperty("path").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.True(doc.RootElement.GetProperty("layout").GetProperty("widgets").GetArrayLength() > 0);
            }

            var custom = HmiLayout.CreateDefault();
            custom.Title = "unit-save";
            custom.Widgets =
            [
                HmiWidgetCatalog.CreateInstance("label", 8, 8, "w-lab"),
            ];
            using (var put = await http.PutAsync(
                       prefix + "/api/hmi/layout",
                       new StringContent(HmiLayoutStore.Serialize(custom), Encoding.UTF8, "application/json")))
            {
                put.EnsureSuccessStatusCode();
            }

            Assert.True(File.Exists(Path.Combine(settingDir, "hmi.layout.json")));
            using (var get2 = await http.GetAsync(prefix + "/api/hmi/layout"))
            {
                var json = await get2.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.Equal("unit-save", doc.RootElement.GetProperty("layout").GetProperty("title").GetString());
                Assert.Equal(1, doc.RootElement.GetProperty("layout").GetProperty("widgets").GetArrayLength());
            }

            using var page = await http.GetAsync(prefix + "/index_hmi.html");
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("hmi_runtime.js", html, StringComparison.Ordinal);
            Assert.Contains("hmiCanvas", html, StringComparison.Ordinal);

            await rt.StopAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
            try { Directory.Delete(settingDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
