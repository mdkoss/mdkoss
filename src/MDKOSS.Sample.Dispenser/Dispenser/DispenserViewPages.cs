namespace MDKOSS.Sample.Dispenser.Machine;

/// <summary>Loads dispenser HTML views from the output <c>views/</c> folder.</summary>
internal static class DispenserViewPages
{
    public static readonly string IndexHtml = Load("indexDispenser.html", "三轴点胶机主界面");

    private static string Load(string fileName, string title)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "views", fileName),
            Path.Combine(AppContext.BaseDirectory, "Dispenser", "views", fileName),
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
<body style="font-family: Segoe UI, sans-serif; padding: 24px; background:#140c08;color:#ffe8d6">
  <h2>{{title}}未找到</h2>
  <p>请将 <code>{{fileName}}</code> 放到程序目录下的 <code>views/</code> 文件夹。</p>
</body>
</html>
""";
    }
}
