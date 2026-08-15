using System.Globalization;
using System.Net;
using System.Text.Json;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// GPIO alias write (<c>POST /api/io/write</c>) and driver-port debug
/// (<c>GET|POST /api/io/driver</c>) for inspecting SIM / card IO words.
/// </summary>
public sealed class IoApiModule : MonitoringApiModule
{
    private sealed class IoWriteRequest
    {
        public string? DeviceId { get; set; }
        public string? Alias { get; set; }
        public bool? Value { get; set; }
    }

    private sealed class DriverIoWriteRequest
    {
        public string? DriverId { get; set; }
        public string? Dir { get; set; }
        public string? Type { get; set; }
        public short? Bit { get; set; }
        public bool? Value { get; set; }
    }

    public IoApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/io";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(remainingPath, "/driver", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDriverIoReadAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDriverIoWriteAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        if (!string.Equals(remainingPath, "/write", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await HandleIoWriteAsync(context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task HandleIoWriteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);

        IoWriteRequest? req;
        try
        {
            req = Deserialize<IoWriteRequest>(body);
        }
        catch (System.Text.Json.JsonException)
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

        if (!Runtime.TryWriteDigitalOutput(req.DeviceId.Trim(), req.Alias.Trim(), req.Value.Value, out var err))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            var payload = System.Text.Json.JsonSerializer.Serialize(
                new { success = false, error = err ?? "write_failed" },
                SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var ok = System.Text.Json.JsonSerializer.Serialize(new { success = true }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleDriverIoReadAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Url?.Query;
        var driverId = GetQueryValue(query, "driverId");
        if (string.IsNullOrWhiteSpace(driverId))
        {
            await WriteErrorAsync(context.Response, "missing_driver_id", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryResolvePort(GetQueryValue(query, "dir"), GetQueryValue(query, "type"), out var isOutput, out var type, out var typeName))
        {
            await WriteErrorAsync(context.Response, "invalid_type", cancellationToken).ConfigureAwait(false);
            return;
        }

        var prefix = isOutput ? "do" : "di";
        var address = $"{prefix}.{typeName}";
        if (!Runtime.TryReadDriverAddress(driverId, address, out var raw, out var err))
        {
            await WriteErrorAsync(context.Response, err ?? "read_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryConvertToInt(raw, out var word))
        {
            await WriteErrorAsync(context.Response, "read_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var cfg = Runtime.Setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, driverId, StringComparison.OrdinalIgnoreCase));
        var ioBitBase = ParseIoBitBase(cfg?.Parameters);
        var bitCount = ResolveBitCount(cfg?.Parameters, isOutput, GetQueryValue(query, "bits"));
        var bits = new object[bitCount];
        for (var shift = 0; shift < bitCount; shift++)
        {
            var addressBit = (short)(shift + ioBitBase);
            bits[shift] = new
            {
                shift,
                addressBit,
                address = $"{prefix}.{typeName}.bit.{addressBit}",
                value = (word & (1 << shift)) != 0,
            };
        }

        Runtime.TryGetDriver(driverId, out var drv);
        var payload = JsonSerializer.Serialize(
            new
            {
                success = true,
                driverId = cfg?.Id ?? driverId.Trim(),
                driverType = cfg?.Type ?? drv?.Name,
                connected = drv?.IsConnected ?? false,
                ioBitBase,
                dir = prefix,
                type,
                typeName,
                address,
                word,
                bitCount,
                bits,
            },
            SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleDriverIoWriteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        DriverIoWriteRequest? req;
        try
        {
            req = Deserialize<DriverIoWriteRequest>(body);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.DriverId) || req.Bit is null || req.Value is null)
        {
            await WriteErrorAsync(context.Response, "missing_fields", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryResolvePort(req.Dir, req.Type, out var isOutput, out _, out var typeName))
        {
            await WriteErrorAsync(context.Response, "invalid_type", cancellationToken).ConfigureAwait(false);
            return;
        }

        var prefix = isOutput ? "do" : "di";
        var address = $"{prefix}.{typeName}.bit.{req.Bit.Value}";
        if (!Runtime.TryWriteDriverAddress(req.DriverId.Trim(), address, req.Value.Value, out var err))
        {
            await WriteErrorAsync(context.Response, err ?? "write_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var ok = JsonSerializer.Serialize(new { success = true, address, value = req.Value.Value }, SnapshotJsonOptions);
        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", ok, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryResolvePort(string? dirRaw, string? typeRaw, out bool isOutput, out short type, out string typeName)
    {
        isOutput = true;
        type = GtsIoType.Gpo;
        typeName = "gpo";

        var dir = (dirRaw ?? "").Trim();
        if (dir.Equals("di", StringComparison.OrdinalIgnoreCase)
            || dir.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            isOutput = false;
            type = GtsIoType.Gpi;
            typeName = "gpi";
        }
        else if (dir.Length > 0
            && !dir.Equals("do", StringComparison.OrdinalIgnoreCase)
            && !dir.Equals("out", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(typeRaw))
        {
            return true;
        }

        if (!GtsIoType.TryResolve(typeRaw, out type))
        {
            return false;
        }

        typeName = TypeName(type);
        return true;
    }

    private static string TypeName(short type) => type switch
    {
        GtsIoType.Gpi => "gpi",
        GtsIoType.Gpo => "gpo",
        GtsIoType.Home => "home",
        GtsIoType.Alarm => "alarm",
        GtsIoType.Enable => "enable",
        GtsIoType.Clear => "clear",
        GtsIoType.Arrive => "arrive",
        GtsIoType.LimitPositive => "limit+",
        GtsIoType.LimitNegative => "limit-",
        _ => type.ToString(CultureInfo.InvariantCulture),
    };

    private static short ParseIoBitBase(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null
            || !parameters.TryGetValue("ioBitBase", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var key = raw.Trim();
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n == 1 ? (short)1 : (short)0;
        }

        return key.Equals("1base", StringComparison.OrdinalIgnoreCase)
            || key.Equals("gts", StringComparison.OrdinalIgnoreCase)
            || key.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? (short)1
            : (short)0;
    }

    private static int ResolveBitCount(IReadOnlyDictionary<string, string>? parameters, bool isOutput, string? bitsQuery)
    {
        if (int.TryParse(bitsQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
        {
            return Math.Clamp(q, 1, 32);
        }

        var key = isOutput ? "outBits" : "inBits";
        if (parameters is not null
            && parameters.TryGetValue(key, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return Math.Clamp(n, 1, 32);
        }

        return 32;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            result = b ? 1 : 0;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
