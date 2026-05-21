namespace MDKOSS.Core.Monitor;

/// <summary>Platform jog / step-teach debug page loader.</summary>
internal static class MonitorPlatformPage
{
    public static readonly string Html = LoadHtml();

    private static string LoadHtml()
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "views", "monitorPlatform.html");
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }

        return """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <title>monitorPlatform.html 缺失</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px; background:#0b1220;color:#dce7ff">
  <h2>平台步进示教页未找到</h2>
  <p>请将 <code>monitorPlatform.html</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }
}
