using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/devices — device listing, detail queries, and action execution.
/// </summary>
public sealed class DevicesApiModule : MonitoringApiModule
{
    private sealed class DeviceActionRequest
    {
        public string? Action { get; set; }
        public Dictionary<string, JsonElement>? Parameters { get; set; }
    }

    public DevicesApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/devices";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var pathRemaining = remainingPath.TrimStart('/');
        var segments = pathRemaining.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isGet = string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var isPost = string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        try
        {
            // GET /api/devices — list all devices
            if (segments.Length == 0 && isGet)
            {
                await HandleDevicesListAsync(context.Response, cancellationToken);
                return true;
            }

            if (segments.Length == 0)
            {
                await WriteErrorAsync(context.Response, "method_not_allowed", cancellationToken);
                return true;
            }

            var deviceId = segments[0];

            // GET /api/devices/{id} — single device detail
            if (segments.Length == 1 && isGet)
            {
                await HandleDeviceGetAsync(context.Response, deviceId, cancellationToken);
                return true;
            }

            // POST /api/devices/{id}/action — execute device action
            if (segments.Length == 2
                && segments[1].Equals("action", StringComparison.OrdinalIgnoreCase)
                && isPost)
            {
                await HandleDeviceActionAsync(context, deviceId, cancellationToken);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken);
            return true;
        }
    }

    private async Task HandleDevicesListAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var snapshot = Runtime.GetSnapshot();
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
        var snapshot = Runtime.GetSnapshot();
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
            axisStatus = dev.AxisStatus,
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
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);

        var req = Deserialize<DeviceActionRequest>(body);
        if (req?.Action is null)
        {
            await WriteErrorAsync(context.Response, "missing_action", cancellationToken);
            return;
        }

        var result = Runtime.ExecuteDeviceAction(deviceId, req.Action, req.Parameters);
        if (!result.Success)
        {
            await WriteErrorAsync(context.Response, result.Error ?? "action_failed", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new { success = true, action = req.Action, result.Data }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken);
    }
}
