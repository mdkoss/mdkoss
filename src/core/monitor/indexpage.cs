namespace MDKOSS.Core.Monitor;

internal static class IndexPage
{
    public static readonly string Html = LoadHtml();

    private static string LoadHtml()
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "views", "index.html");
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }

        return """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>主界面缺失</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px;">
  <h2>主界面模板缺失</h2>
  <p>请将 <code>index.html</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }
}
