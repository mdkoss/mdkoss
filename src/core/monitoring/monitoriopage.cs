namespace MDKOSS.Core.Monitoring;

internal static class MonitorIoPage
{
    public static readonly string Html = LoadHtml();

    private static string LoadHtml()
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "views", "monitorIO.html");
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }

        return """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <title>monitorIO.html 缺失</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px; background:#0b1220;color:#dce7ff">
  <h2>IO 监控页未找到</h2>
  <p>请将 <code>monitorIO.html</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }
}
