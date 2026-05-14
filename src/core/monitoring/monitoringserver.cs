using System.Net;
using System.Text;
using System.Text.Json;
namespace MDKOSS.Core.Monitoring;

public sealed class MonitoringServer : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions IoWriteJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class IoWriteRequest
    {
        public string? DeviceId { get; set; }

        public string? Alias { get; set; }

        public bool? Value { get; set; }
    }

    private readonly HttpListener _listener = new();
    private readonly MdkRuntime _runtime;
    private readonly string _prefix;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public MonitoringServer(MdkRuntime runtime, string prefix = "http://localhost:5080/")
    {
        _runtime = runtime;
        _prefix = NormalizePrefix(prefix);
        AddListenerPrefixes(_listener, _prefix);
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
        _listener.Stop();
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
        var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(_runtime.GetSnapshot(), SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path.Equals("/api/io/write", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleIoWriteAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/monitorIO.html", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", MonitorIoPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", MonitoringPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Not Found", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleIoWriteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        IoWriteRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<IoWriteRequest>(body, IoWriteJsonOptions);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(
                    context.Response,
                    "application/json; charset=utf-8",
                    """{"success":false,"error":"invalid_json"}""",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (req is null
            || string.IsNullOrWhiteSpace(req.DeviceId)
            || string.IsNullOrWhiteSpace(req.Alias)
            || req.Value is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(
                    context.Response,
                    "application/json; charset=utf-8",
                    """{"success":false,"error":"missing_fields"}""",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!_runtime.TryWriteDigitalOutput(req.DeviceId.Trim(), req.Alias.Trim(), req.Value.Value, out var err))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            var payload = JsonSerializer.Serialize(
                new { success = false, error = err ?? "write_failed" },
                SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var ok = JsonSerializer.Serialize(new { success = true }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
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

    private static Task WriteTaskOperationResultAsync(
        HttpListenerResponse response,
        bool success,
        string action,
        CancellationToken cancellationToken)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new
        {
            success,
            action,
            timestampUtc = DateTime.UtcNow
        });
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    public void Dispose()
    {
        _listener.Close();
        _cts?.Dispose();
    }
}
