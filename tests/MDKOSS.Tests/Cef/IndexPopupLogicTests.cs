namespace MDKOSS.Tests.Cef;

/// <summary>Contracts for index.html secondary popup shell (Issue #40).</summary>
public sealed class IndexPopupLogicTests
{
    [Fact]
    public void Index_popup_toggles_same_entry_and_tracks_key()
    {
        var html = File.ReadAllText(FindViewsFile("index.html"));
        Assert.Contains("currentPopupKey", html, StringComparison.Ordinal);
        Assert.Contains("buildPopupKey", html, StringComparison.Ordinal);
        Assert.Contains("syncPopupNav", html, StringComparison.Ordinal);
        Assert.Contains("currentPopupKey === key", html, StringComparison.Ordinal);
        Assert.Contains("needReload", html, StringComparison.Ordinal);
        Assert.Contains("mdkoss-popup-close", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Popup_modal_leaves_topbar_clickable()
    {
        var css = File.ReadAllText(FindViewsFile(Path.Combine("css", "main.css")));
        var idx = css.IndexOf(".popup-modal {", StringComparison.Ordinal);
        Assert.True(idx >= 0, "missing .popup-modal rule");
        var slice = css.Substring(idx, Math.Min(280, css.Length - idx));
        Assert.Contains("top: var(--top-h)", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("inset: 0", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void Popup_embed_script_forwards_escape_and_is_wired()
    {
        var embed = File.ReadAllText(FindViewsFile(Path.Combine("js", "popup_embed.js")));
        Assert.Contains("mdkoss-popup-close", embed, StringComparison.Ordinal);
        Assert.Contains("Escape", embed, StringComparison.Ordinal);
        Assert.Contains("embedded", embed, StringComparison.Ordinal);

        foreach (var name in new[]
                 {
                     "popup_devices.html", "popup_tasks.html", "popup_vars.html", "popup_alarms.html",
                     "popup_order.html", "popup_recipe.html", "popup_user.html", "popup_about.html"
                 })
        {
            var html = File.ReadAllText(FindViewsFile(name));
            Assert.Contains("/js/popup_embed.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "URLSearchParams(location.search).get(\"embedded\") === \"1\") document.body.classList.add(\"embedded\")",
                html,
                StringComparison.Ordinal);
        }
    }

    private static string FindViewsFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MDKOSS.Cef", "views", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MDKOSS.Cef", "views", relative));
    }
}
