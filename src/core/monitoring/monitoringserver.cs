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

    private sealed class SerialPortConfigRequest
    {
        public string? PortName { get; set; }
        public int? BaudRate { get; set; }
        public int? DataBits { get; set; }
        public string? Parity { get; set; }
        public string? StopBits { get; set; }
        public int? ReadTimeout { get; set; }
        public int? WriteTimeout { get; set; }
        public bool? DtrEnable { get; set; }
        public bool? RtsEnable { get; set; }
    }

    private sealed class SerialOpenRequest
    {
        public string? DeviceId { get; set; }
        public SerialPortConfigRequest? Config { get; set; }
    }

    private sealed class SerialWriteRequest
    {
        public string? DeviceId { get; set; }
        public string? Data { get; set; }
    }

    private sealed class SerialWriteBinRequest
    {
        public string? DeviceId { get; set; }
        public byte[]? Data { get; set; }
    }

    private sealed class DeviceActionRequest
    {
        public string? Action { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
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

        if (path.StartsWith("/api/devices", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDevicesAsync(context, path, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/monitorIO.html", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", MonitorIoPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path.Equals("/debugSerialDev.html", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", DebugSerialDevPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path.Equals("/monitorPlatform.html", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", MonitorPlatformPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path.StartsWith("/api/serial/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSerialAsync(context, path, cancellationToken).ConfigureAwait(false);
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

    private async Task HandleDevicesAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken)
    {
        var pathRemaining = path["/api/devices".Length..].TrimStart('/');
        var segments = pathRemaining.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isGet = string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            // GET /api/devices - 列出所有设备
            if (segments.Length == 0 && isGet)
            {
                await HandleDevicesListAsync(context.Response, cancellationToken);
                return;
            }

            if (segments.Length == 0)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return;
            }

            var deviceId = segments[0];

            // GET /api/devices/{id} - 获取单个设备详情
            if (segments.Length == 1 && isGet)
            {
                await HandleDeviceGetAsync(context.Response, deviceId, cancellationToken);
                return;
            }

            // POST /api/devices/{id}/action - 执行设备操作
            if (segments.Length == 2 && segments[1].Equals("action", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                await HandleDeviceActionAsync(context, deviceId, cancellationToken);
                return;
            }

            await WriteErrorAsync(context.Response, "unknown_endpoint", cancellationToken);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken);
        }
    }

    private async Task HandleDevicesListAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var snapshot = _runtime.GetSnapshot();
        var devices = new List<object>();
        foreach (var (id, dev) in snapshot.Devices)
        {
            devices.Add(new
            {
                id,
                name = dev.Name,
                type = dev.Type,
                state = dev.State,
                driverType = dev.DriverType,
                driverConnected = dev.DriverConnected,
                serialPortInfo = dev.SerialPortInfo != null ? new
                {
                    isOpen = dev.SerialPortInfo.IsOpen,
                    portName = dev.SerialPortInfo.PortName,
                    baudRate = dev.SerialPortInfo.BaudRate,
                    bytesToRead = dev.SerialPortInfo.BytesToRead
                } : null
            });
        }

        var payload = JsonSerializer.Serialize(new { success = true, devices }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleDeviceGetAsync(HttpListenerResponse response, string deviceId, CancellationToken cancellationToken)
    {
        var snapshot = _runtime.GetSnapshot();
        if (!snapshot.Devices.TryGetValue(deviceId, out var dev))
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken);
            return;
        }

        var deviceInfo = new
        {
            id = dev.Id,
            name = dev.Name,
            type = dev.Type,
            state = dev.State,
            driverType = dev.DriverType,
            driverConnected = dev.DriverConnected,
            gpioIoPoints = dev.GpioIoPoints,
            platformAxes = dev.PlatformAxes,
            serialPortInfo = dev.SerialPortInfo != null ? new
            {
                isOpen = dev.SerialPortInfo.IsOpen,
                portName = dev.SerialPortInfo.PortName,
                baudRate = dev.SerialPortInfo.BaudRate,
                dataBits = dev.SerialPortInfo.DataBits,
                parity = dev.SerialPortInfo.Parity,
                stopBits = dev.SerialPortInfo.StopBits,
                bytesToRead = dev.SerialPortInfo.BytesToRead
            } : null
        };

        var payload = JsonSerializer.Serialize(new { success = true, device = deviceInfo }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleDeviceActionAsync(HttpListenerContext context, string deviceId, CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var req = JsonSerializer.Deserialize<DeviceActionRequest>(body, IoWriteJsonOptions);
        if (req?.Action is null)
        {
            await WriteErrorAsync(context.Response, "missing_action", cancellationToken);
            return;
        }

        var result = _runtime.ExecuteDeviceAction(deviceId, req.Action, req.Parameters);
        if (!result.Success)
        {
            await WriteErrorAsync(context.Response, result.Error ?? "action_failed", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, action = req.Action, result.Data }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken)
    {
        var actionPath = path["/api/serial/".Length..].Trim('/');
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            // GET /api/serial/status?deviceId=xxx
            if (actionPath.Equals("status", StringComparison.OrdinalIgnoreCase) && !isPost)
            {
                var query = context.Request.QueryString?["deviceId"];
                if (string.IsNullOrWhiteSpace(query))
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return;
                }
                await HandleSerialStatusAsync(context.Response, query, cancellationToken);
                return;
            }

            if (!isPost)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            // POST /api/serial/open
            if (actionPath.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialOpenRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return;
                }
                await HandleSerialOpenAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return;
            }

            // POST /api/serial/close
            if (actionPath.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialWriteRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return;
                }
                await HandleSerialCloseAsync(context.Response, req.DeviceId, cancellationToken);
                return;
            }

            // POST /api/serial/config
            if (actionPath.Equals("config", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialOpenRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null || req.Config is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return;
                }
                await HandleSerialConfigAsync(context.Response, req.DeviceId, req.Config, cancellationToken);
                return;
            }

            // POST /api/serial/write
            if (actionPath.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialWriteRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return;
                }
                await HandleSerialWriteAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return;
            }

            // POST /api/serial/writeBin
            if (actionPath.Equals("writeBin", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialWriteBinRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null || req.Data is null)
                {
                    await WriteErrorAsync(context.Response, "missing_fields", cancellationToken);
                    return;
                }
                await HandleSerialWriteBinAsync(context.Response, req.DeviceId, req.Data, cancellationToken);
                return;
            }

            // POST /api/serial/read
            if (actionPath.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialWriteRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return;
                }
                await HandleSerialReadAsync(context.Response, req.DeviceId, cancellationToken);
                return;
            }

            // POST /api/serial/discard
            if (actionPath.Equals("discard", StringComparison.OrdinalIgnoreCase))
            {
                var req = JsonSerializer.Deserialize<SerialWriteRequest>(body, IoWriteJsonOptions);
                if (req?.DeviceId is null)
                {
                    await WriteErrorAsync(context.Response, "missing_device_id", cancellationToken);
                    return;
                }
                await HandleSerialDiscardAsync(context.Response, req.DeviceId, cancellationToken);
                return;
            }

            await WriteErrorAsync(context.Response, "unknown_action", cancellationToken);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken);
        }
    }

    private Task WriteErrorAsync(HttpListenerResponse response, string error, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new { success = false, error }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialStatusAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var result = _runtime.GetSerialStatus(deviceId.Trim());
        if (result is null)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(result, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialOpenAsync(
        HttpListenerResponse response,
        string deviceId,
        SerialPortConfigRequest config,
        CancellationToken cancellationToken)
    {
        var err = _runtime.OpenSerialPort(deviceId.Trim(), ToConfig(config));
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "port_opened", cancellationToken);
    }

    private async Task HandleSerialCloseAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = _runtime.CloseSerialPort(deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "port_closed", cancellationToken);
    }

    private async Task HandleSerialConfigAsync(
        HttpListenerResponse response,
        string deviceId,
        SerialPortConfigRequest config,
        CancellationToken cancellationToken)
    {
        var err = _runtime.SetSerialConfig(deviceId.Trim(), ToConfig(config));
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "config_applied", cancellationToken);
    }

    private async Task HandleSerialWriteAsync(
        HttpListenerResponse response,
        string deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        var err = _runtime.WriteSerialText(deviceId.Trim(), data);
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleSerialWriteBinAsync(
        HttpListenerResponse response,
        string deviceId,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var err = _runtime.WriteSerialBinary(deviceId.Trim(), data);
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "data_sent", cancellationToken);
    }

    private async Task HandleSerialReadAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var (err, data) = _runtime.ReadSerialAll(deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, data }, SnapshotJsonOptions);
        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private async Task HandleSerialDiscardAsync(
        HttpListenerResponse response,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var err = _runtime.DiscardSerialBuffers(deviceId.Trim());
        if (err != SerialErrorCode.Ok)
        {
            await WriteErrorAsync(response, $"error_{err}", cancellationToken);
            return;
        }

        await WriteSuccessAsync(response, "buffers_discarded", cancellationToken);
    }

    private static Task WriteSuccessAsync(HttpListenerResponse response, string action, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { success = true, action }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    private static SerialPortConfig ToConfig(SerialPortConfigRequest req)
    {
        return new SerialPortConfig
        {
            PortName = req.PortName ?? "COM1",
            BaudRate = req.BaudRate ?? 9600,
            DataBits = req.DataBits ?? 8,
            Parity = Enum.Parse<SerialParity>(req.Parity ?? "None"),
            StopBits = Enum.Parse<SerialStopBits>(req.StopBits ?? "One"),
            ReadTimeout = req.ReadTimeout ?? 5000,
            WriteTimeout = req.WriteTimeout ?? 5000,
            DtrEnable = req.DtrEnable ?? false,
            RtsEnable = req.RtsEnable ?? false
        };
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
