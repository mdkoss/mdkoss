namespace MDKOSS.Cef.Extensions;

/// <summary>Loads HMI HTML from host <c>views/</c> or the plugin output folder.</summary>
internal static class HmiViewPages
{
    public static string IndexHtml => Load("index_hmi.html", "主界面监控组态");

    public static string EditorHtml => Load("man_hmi.html", "主界面组态编辑");

    private static string Load(string fileName, string title)
    {
        foreach (var fullPath in EnumerateCandidates(fileName))
        {
            if (File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }
        }

        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <title>{{title}} 缺失</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px; background:#0b1220;color:#dce7ff">
  <h2>{{title}}未找到</h2>
  <p>请将 <code>{{fileName}}</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }

    private static IEnumerable<string> EnumerateCandidates(string fileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "views", fileName);

        var asmDir = Path.GetDirectoryName(typeof(HmiViewPages).Assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            yield return Path.Combine(asmDir, "views", fileName);
        }
    }
}
