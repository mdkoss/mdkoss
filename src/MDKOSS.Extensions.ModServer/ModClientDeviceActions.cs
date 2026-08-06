using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Unified action handlers for <see cref="ModClientDevice"/>.</summary>
internal static class ModClientDeviceActions
{
    internal static DeviceActionResult Execute(
        ModClientDevice device,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "connect" or "open" =>
                ToResult(device.Connect(), "connect_failed", () => StatusPayload(device)),
            "disconnect" or "close" =>
                ToResult(device.Disconnect(), "disconnect_failed", () => StatusPayload(device)),
            "status" => DeviceActionResult.Ok(StatusPayload(device)),
            "readholding" or "read_holding" => ReadHolding(device, parameters),
            "writeholding" or "write_holding" => WriteHolding(device, parameters),
            "readinput" or "read_input" => ReadInput(device, parameters),
            "readcoils" or "read_coils" => ReadCoils(device, parameters),
            "writecoils" or "write_coils" => WriteCoils(device, parameters),
            "readdiscrete" or "read_discrete" => ReadDiscrete(device, parameters),
            "readbatch" or "read_batch" or "batchread" or "batch_read" => ReadBatch(device, parameters),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static object StatusPayload(ModClientDevice device) => new
    {
        device.Id,
        isConnected = device.IsConnected,
        host = device.Parameters.Host,
        port = device.Parameters.Port,
        unitId = device.Parameters.UnitId,
        autoConnect = device.Parameters.AutoConnect,
        lastError = device.LastError,
    };

    private static DeviceActionResult ReadHolding(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadHoldingRegisters(address, count);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteHolding(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressUshorts(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteHoldingRegisters(address, values!);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadInput(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadInputRegisters(address, count);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadCoils(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadCoils(address, count);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteCoils(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressBools(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteCoils(address, values!);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadDiscrete(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadDiscreteInputs(address, count);
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadBatch(ModClientDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null
            || !parameters.TryGetValue("items", out var itemsEl)
            || itemsEl.ValueKind != JsonValueKind.Array)
        {
            return DeviceActionResult.Fail("missing_items");
        }

        var items = new List<ModClientReadItem>();
        foreach (var el in itemsEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                return DeviceActionResult.Fail("invalid_items");
            }

            if (!TryParseReadItem(el, out var item, out var error))
            {
                return DeviceActionResult.Fail(error!);
            }

            items.Add(item!);
        }

        if (items.Count == 0)
        {
            return DeviceActionResult.Fail("empty_items");
        }

        var results = device.ReadBatch(items);
        return DeviceActionResult.Ok(new
        {
            count = results.Count,
            ok = results.Count(r => r.Error == ModClientErrorCode.Ok),
            results = results.Select(ToBatchPayload),
        });
    }

    private static object ToBatchPayload(ModClientReadResult r) => new
    {
        tag = r.Tag,
        area = AreaToToken(r.Area),
        address = r.Address,
        count = r.Count,
        values = r.Registers is not null ? (object)r.Registers : r.Bits,
        error = r.Error == ModClientErrorCode.Ok ? null : (r.ErrorMessage ?? r.Error.ToString()),
        success = r.Error == ModClientErrorCode.Ok,
    };

    private static bool TryParseReadItem(JsonElement el, out ModClientReadItem? item, out string? error)
    {
        item = null;
        error = null;

        if (!TryGetPropertyUInt16(el, "address", out var address))
        {
            error = "missing_address";
            return false;
        }

        ushort count = 1;
        if (el.TryGetProperty("count", out _) || el.TryGetProperty("Count", out _))
        {
            if (!TryGetPropertyUInt16(el, "count", out count) || count == 0)
            {
                error = "invalid_count";
                return false;
            }
        }

        string? areaRaw = null;
        if (el.TryGetProperty("area", out var areaEl) || el.TryGetProperty("kind", out areaEl)
            || el.TryGetProperty("type", out areaEl))
        {
            areaRaw = areaEl.ValueKind == JsonValueKind.String ? areaEl.GetString() : null;
        }

        if (!TryParseArea(areaRaw, out var area))
        {
            error = "invalid_area";
            return false;
        }

        string? tag = null;
        if (el.TryGetProperty("tag", out var tagEl) && tagEl.ValueKind == JsonValueKind.String)
        {
            tag = tagEl.GetString();
        }

        item = new ModClientReadItem
        {
            Area = area,
            Address = address,
            Count = count,
            Tag = tag,
        };
        return true;
    }

    internal static bool TryParseArea(string? raw, out ModClientArea area)
    {
        area = ModClientArea.Holding;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // default holding
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "holding":
            case "hold":
            case "hr":
            case "4x":
            case "register":
            case "registers":
                area = ModClientArea.Holding;
                return true;
            case "input":
            case "ir":
            case "3x":
            case "inputregister":
            case "inputregisters":
                area = ModClientArea.Input;
                return true;
            case "coil":
            case "coils":
            case "0x":
                area = ModClientArea.Coils;
                return true;
            case "discrete":
            case "di":
            case "1x":
            case "inputstatus":
                area = ModClientArea.Discrete;
                return true;
            default:
                return false;
        }
    }

    internal static string AreaToToken(ModClientArea area) => area switch
    {
        ModClientArea.Holding => "holding",
        ModClientArea.Input => "input",
        ModClientArea.Coils => "coils",
        ModClientArea.Discrete => "discrete",
        _ => "holding",
    };

    private static DeviceActionResult ToResult(
        ModClientErrorCode code,
        string failToken,
        Func<object> okPayload)
    {
        return code == ModClientErrorCode.Ok
            ? DeviceActionResult.Ok(okPayload())
            : DeviceActionResult.Fail($"{failToken}:{code}");
    }

    private static bool TryGetAddressCount(
        Dictionary<string, JsonElement>? parameters,
        out ushort address,
        out ushort count,
        out string? error)
    {
        address = 0;
        count = 1;
        error = null;

        if (parameters is null || !TryGetUInt16(parameters, "address", out address))
        {
            error = "missing_address";
            return false;
        }

        if (parameters.TryGetValue("count", out _))
        {
            if (!TryGetUInt16(parameters, "count", out count) || count == 0)
            {
                error = "invalid_count";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetAddressUshorts(
        Dictionary<string, JsonElement>? parameters,
        out ushort address,
        out ushort[]? values,
        out string? error)
    {
        address = 0;
        values = null;
        error = null;

        if (parameters is null || !TryGetUInt16(parameters, "address", out address))
        {
            error = "missing_address";
            return false;
        }

        if (!parameters.TryGetValue("values", out var valuesEl) || valuesEl.ValueKind != JsonValueKind.Array)
        {
            error = "missing_values";
            return false;
        }

        var list = new List<ushort>();
        foreach (var item in valuesEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetUInt16(out var u))
            {
                list.Add(u);
            }
            else if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var i) && i is >= 0 and <= ushort.MaxValue)
            {
                list.Add((ushort)i);
            }
            else
            {
                error = "invalid_values";
                return false;
            }
        }

        if (list.Count == 0)
        {
            error = "empty_values";
            return false;
        }

        values = list.ToArray();
        return true;
    }

    private static bool TryGetAddressBools(
        Dictionary<string, JsonElement>? parameters,
        out ushort address,
        out bool[]? values,
        out string? error)
    {
        address = 0;
        values = null;
        error = null;

        if (parameters is null || !TryGetUInt16(parameters, "address", out address))
        {
            error = "missing_address";
            return false;
        }

        if (!parameters.TryGetValue("values", out var valuesEl) || valuesEl.ValueKind != JsonValueKind.Array)
        {
            error = "missing_values";
            return false;
        }

        var list = new List<bool>();
        foreach (var item in valuesEl.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                list.Add(item.GetBoolean());
            }
            else if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n))
            {
                list.Add(n != 0);
            }
            else
            {
                error = "invalid_values";
                return false;
            }
        }

        if (list.Count == 0)
        {
            error = "empty_values";
            return false;
        }

        values = list.ToArray();
        return true;
    }

    private static bool TryGetUInt16(Dictionary<string, JsonElement> parameters, string key, out ushort value)
    {
        value = 0;
        if (!parameters.TryGetValue(key, out var el))
        {
            return false;
        }

        return TryParseUInt16(el, out value);
    }

    private static bool TryGetPropertyUInt16(JsonElement obj, string key, out ushort value)
    {
        value = 0;
        if (obj.TryGetProperty(key, out var el) || obj.TryGetProperty(char.ToUpperInvariant(key[0]) + key[1..], out el))
        {
            return TryParseUInt16(el, out value);
        }

        return false;
    }

    private static bool TryParseUInt16(JsonElement el, out ushort value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt16(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) && i is >= 0 and <= ushort.MaxValue)
        {
            value = (ushort)i;
            return true;
        }

        if (el.ValueKind == JsonValueKind.String && ushort.TryParse(el.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}
