namespace MDKOSS.Core.Monitor;

/// <summary>Loads HTML from <c>{BaseDirectory}/views/</c>.</summary>
internal static class ViewsHtml
{
    public static string? TryLoad(string fileName)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "views", fileName);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    public static string Load(string fileName, string missingTitle)
    {
        var html = TryLoad(fileName);
        if (html is not null)
        {
            return html;
        }

        return $"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <title>{missingTitle} 缺失</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px; background:#0b1220;color:#dce7ff">
  <h2>{missingTitle} 未找到</h2>
  <p>请将 <code>{fileName}</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }
}
