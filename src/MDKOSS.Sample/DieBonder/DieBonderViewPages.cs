namespace MDKOSS.Sample.DieBonder;

/// <summary>Loads Die Bonder HTML views from the output <c>views/</c> folder.</summary>
internal static class DieBonderViewPages
{
    public static readonly string IndexHtml = Load("indexDieBonder.html", "半导体贴片机主界面");

    public static readonly string MonitorHtml = Load("monitorDieBonder.html", "贴片循环监控");

    private static string Load(string fileName, string title)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "views", fileName),
            Path.Combine(AppContext.BaseDirectory, "DieBonder", "views", fileName),
        };

        foreach (var fullPath in candidates)
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
}
