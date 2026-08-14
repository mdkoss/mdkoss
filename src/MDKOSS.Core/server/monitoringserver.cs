using System.Net;
using System.Text;

namespace MDKOSS.Core.Monitor;

public sealed class MonitoringServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly List<MonitoringApiModule> _modules = [];
    private readonly Dictionary<string, string> _staticPages;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public MonitoringServer(MdkRuntime runtime, string prefix = "http://localhost:5080/")
    {
        _prefix = NormalizePrefix(prefix);
        AddListenerPrefixes(_listener, _prefix);

        // Register API modules (order matters: more specific prefixes first)
        _modules.Add(new StatusApiModule(runtime));
        _modules.Add(new IoApiModule(runtime));
        _modules.Add(new DevicesApiModule(runtime));
        _modules.AddRange(MonitoringModuleRegistry.CreateModules(runtime));
        _modules.Add(new RecipeApiModule(runtime));
        _modules.Add(new OrdersApiModule(runtime));
        _modules.Add(new TeachApiModule(runtime));
        _modules.Add(new DbApiModule(runtime));
        _modules.Add(new AlarmsApiModule(runtime));
        _modules.Add(new VisionsApiModule(runtime));
        _modules.Add(new ConfigApiModule(runtime));
        _modules.Add(new TasksApiModule(runtime));
        _modules.Add(new TaskApiModule(runtime));

        // Static HTML pages (built-in + extension / machine registrations)
        // Canonical names use snake_case; legacy camelCase URLs kept as aliases.
        var monitorRuntime = MonitoringPage.Html;
        var monitorIo = MonitorIoPage.Html;
        var debugPlatform = MonitorPlatformPage.Html;
        var debugSerial = DebugSerialDevPage.Html;
        var debugDb = DebugDbPage.Html;
        _staticPages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = IndexPage.Html,
            ["/index.html"] = IndexPage.Html,

            ["/monitor_runtime.html"] = monitorRuntime,
            ["/monitoringpage.html"] = monitorRuntime,

            ["/monitor_io.html"] = monitorIo,
            ["/monitorIO.html"] = monitorIo,

            ["/debug_platform.html"] = debugPlatform,
            ["/monitorPlatform.html"] = debugPlatform,

            ["/debug_serial.html"] = debugSerial,
            ["/debugSerialDev.html"] = debugSerial,
            ["/debugserialdev.html"] = debugSerial,

            ["/debug_db.html"] = debugDb,
            ["/debugdb.html"] = debugDb,
        };
        RegisterViewsPage(_staticPages, "/monitor_platform.html");
        RegisterViewsPage(_staticPages, "/monitor_axis.html");
        RegisterViewsPage(_staticPages, "/monitor_camera.html");
        RegisterViewsPage(_staticPages, "/monitor_task.html");
        RegisterViewsPage(_staticPages, "/monitor_alarm.html");
        RegisterViewsPage(_staticPages, "/monitor_vision.html");
        RegisterViewsPage(_staticPages, "/debug_axis.html");
        RegisterViewsPage(_staticPages, "/debug_camera.html");
        RegisterViewsPage(_staticPages, "/debug_driver.html");
        RegisterViewsPage(_staticPages, "/debug_io.html");
        RegisterViewsPage(_staticPages, "/debug_machine.html");
        RegisterViewsPage(_staticPages, "/debug_alarm.html");
        RegisterViewsPage(_staticPages, "/debug_vision.html");
        RegisterViewsPage(_staticPages, "/man_driver.html");
        RegisterViewsPage(_staticPages, "/man_device.html");
        RegisterViewsPage(_staticPages, "/man_axis.html");
        RegisterViewsPage(_staticPages, "/man_platform.html");
        RegisterViewsPage(_staticPages, "/man_gpio.html");
        RegisterViewsPage(_staticPages, "/man_recipe.html");
        RegisterViewsPage(_staticPages, "/man_task.html");
        RegisterViewsPage(_staticPages, "/man_alarm.html");
        RegisterViewsPage(_staticPages, "/man_vision.html");
        RegisterViewsPage(_staticPages, "/man_vars.html");
        RegisterViewsPage(_staticPages, "/man_machine.html");
        RegisterViewsPage(_staticPages, "/popup_devices.html");
        RegisterViewsPage(_staticPages, "/popup_tasks.html");
        RegisterViewsPage(_staticPages, "/popup_vars.html");
        RegisterViewsPage(_staticPages, "/popup_alarms.html");
        RegisterViewsPage(_staticPages, "/popup_order.html");
        RegisterViewsPage(_staticPages, "/popup_recipe.html");
        RegisterViewsPage(_staticPages, "/popup_user.html");
        RegisterViewsPage(_staticPages, "/popup_about.html");
        foreach (var (path, html) in StaticPageRegistry.CreatePages())
        {
            _staticPages[path] = html;
        }
    }

    /// <summary>
    /// Register an additional API module. Must be called before <see cref="Start"/>.
    /// </summary>
    public void AddModule(MonitoringApiModule module)
    {
        _modules.Add(module);
    }

    private static void RegisterViewsPage(Dictionary<string, string> pages, string path)
    {
        var fileName = path.TrimStart('/');
        pages[path] = ViewsHtml.Load(fileName, fileName);
    }

    private static string NormalizePrefix(string prefix)
    {
        prefix = prefix.Trim();
        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }

    /// <summary>
    /// HttpListener matches the request host strictly. Register loopback aliases so
    /// <c>http://127.0.0.1:5080/</c> works when the primary prefix uses <c>localhost</c> (avoids HTTP 400 Invalid Hostname).
    /// </summary>
    private static void AddListenerPrefixes(HttpListener listener, string primaryPrefix)
    {
        listener.Prefixes.Add(primaryPrefix);
        if (!Uri.TryCreate(primaryPrefix, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Host is "*" or "+")
        {
            return;
        }

        var portSegment = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var path = uri.AbsolutePath;
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        var scheme = uri.Scheme;
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryPrefix };
        void AddIfDistinct(string host)
        {
            var p = $"{scheme}://{host}{portSegment}{path}";
            if (added.Add(p))
            {
                listener.Prefixes.Add(p);
            }
        }

        // Do not register http://[::1]:... alongside localhost/127.0.0.1: on Windows, http.sys often
        // reports "conflicts with an existing registration" when IPv4 and IPv6 loopback share the same port.
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            AddIfDistinct("127.0.0.1");
        }
        else if (uri.Host == "127.0.0.1")
        {
            AddIfDistinct("localhost");
        }
        else if (uri.Host is "[::1]" or "::1")
        {
            AddIfDistinct("localhost");
            AddIfDistinct("127.0.0.1");
        }
    }

    public string Prefix => _prefix;

    public void Start()
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Monitoring server has already started.");
        }

        _cts = new CancellationTokenSource();
        _listener.Start();
        _loopTask = ListenLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (HttpListenerException)
        {
            // Listener may not have been successfully started; ignore cleanup error.
        }
        if (_loopTask is not null)
        {
            await _loopTask.ConfigureAwait(false);
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException)
            {
                // Listener is likely stopped.
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponseAsync(
                        context.Response,
                        "application/json; charset=utf-8",
                        $"{{\"success\":false,\"error\":\"server_error\",\"message\":\"{ex.GetType().Name}\"}}",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Response may already be closed.
            }
        }
    }

    private async Task HandleCoreAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        // 1. Try API modules
        foreach (var module in _modules)
        {
            if (path.StartsWith(module.RoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainingPath = path[module.RoutePrefix.Length..];
                if (await module.HandleAsync(context, remainingPath, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        // 2. Static pages — prefer live views/ files so Sample always serves current HTML
        if (_staticPages.TryGetValue(path, out var html))
        {
            var fileName = string.IsNullOrEmpty(path) || path == "/" ? "index.html" : path.TrimStart('/');
            html = ViewsHtml.TryLoad(fileName) ?? html;
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // 3. Static assets under views/ (css / js / images)
        var assetPath = context.Request.Url?.AbsolutePath ?? path;
        if (await TryServeViewsAssetAsync(context.Response, assetPath, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // 4. 404
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Not Found", cancellationToken)
            .ConfigureAwait(false);
    }

    private static readonly HashSet<string> ViewsAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".woff", ".woff2",
    };

    /// <summary>
    /// Serves files from <c>{BaseDirectory}/views/</c> for relative URLs such as
    /// <c>/css/main.css</c> or <c>/debug_platform.js</c>.
    /// </summary>
    private static async Task<bool> TryServeViewsAssetAsync(
        HttpListenerResponse response,
        string requestPath,
        CancellationToken cancellationToken)
    {
        var relative = Uri.UnescapeDataString(requestPath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative) ||
            relative.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return false;
        }

        var ext = Path.GetExtension(relative);
        if (string.IsNullOrEmpty(ext) || !ViewsAssetExtensions.Contains(ext))
        {
            return false;
        }

        var viewsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "views"));
        var fullPath = Path.GetFullPath(Path.Combine(viewsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = viewsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return false;
        }

        var contentType = ext.ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
        return true;
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        _listener.Close();
        _cts?.Dispose();
    }
}
