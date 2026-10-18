using MDKOSS.Cef.Extensions;

namespace MDKOSS.Tests.Cef;

public sealed class DemoBuiltinLayoutTests
{
    private static readonly string[] BuiltinWidgets = ["label", "value", "lamp", "progress", "status", "button"];

    public static IEnumerable<object[]> Layouts =>
    [
        ["src/MDKOSS.Cef.Sample/configs/hmi.layout.json"],
        ["src/MDKOSS.UI.WPF/configs/hmi.layout.json"],
        ["src/MDKOSS.UI.WPF.Sample/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.DieBonder/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.Dispenser/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.Pnp/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.Tools/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.Modbus/configs/hmi.layout.json"],
        ["src/MDKOSS.Config.Wpf/configs/hmi.layout.json"],
        ["src/MDKOSS.Sample.Iec61131/configs/hmi.layout.json"],
    ];

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Each_demo_layout_uses_builtin_widgets_and_live_actions(string relative)
    {
        var path = FindRepoFile(relative);
        var layout = HmiLayoutStore.Parse(File.ReadAllText(path));
        Assert.NotNull(layout);
        var types = layout!.Widgets.Select(w => w.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in BuiltinWidgets)
        {
            Assert.Contains(widget, types);
        }

        var buttons = layout.Widgets.Where(w => w.Type == "button").ToList();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            Assert.True(button.Props.TryGetValue("url", out var url));
            var text = url?.ToString() ?? "";
            Assert.StartsWith("/api/", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(layout.Widgets, w =>
            w.Type is "value" or "lamp" or "progress" or "status"
            && w.Props.TryGetValue("var", out var key)
            && !string.IsNullOrWhiteSpace(key?.ToString()));
    }

    [Fact]
    public void Cef_debug_pages_cover_tcp_python_and_modbus()
    {
        var server = File.ReadAllText(FindRepoFile("src/MDKOSS.Core/server/monitoringserver.cs"));
        foreach (var page in new[] { "debug_tcp.html", "debug_pyscript.html", "debug_modbus.html" })
        {
            Assert.Contains($"\"/{page}\"", server, StringComparison.Ordinal);
            var html = File.ReadAllText(FindRepoFile("src/MDKOSS.Cef/views/" + page));
            Assert.Contains("data-tool-page=", html, StringComparison.Ordinal);
        }

        Assert.Contains("/api/tcp/", File.ReadAllText(FindRepoFile("src/MDKOSS.Cef/views/debug_tcp.html")));
        Assert.Contains("/api/pyscript/", File.ReadAllText(FindRepoFile("src/MDKOSS.Cef/views/debug_pyscript.html")));
        Assert.Contains("/api/modserver", File.ReadAllText(FindRepoFile("src/MDKOSS.Cef/views/debug_modbus.html")));
        Assert.Contains("/api/modclient", File.ReadAllText(FindRepoFile("src/MDKOSS.Cef/views/debug_modbus.html")));
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
