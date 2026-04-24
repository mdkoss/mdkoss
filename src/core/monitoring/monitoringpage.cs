namespace MDKOSS.Core.Monitoring;

internal static class MonitoringPage
{
    public static readonly string Html = LoadHtml();

    private static string LoadHtml()
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "views", "monitoringpage.html");
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }

        return """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Monitoring Page Missing</title>
</head>
<body style="font-family: Segoe UI, sans-serif; padding: 24px;">
  <h2>Monitoring page template missing</h2>
  <p>Expected file: <code>src/views/monitoringpage.html</code></p>
</body>
</html>
""";
    }
}
