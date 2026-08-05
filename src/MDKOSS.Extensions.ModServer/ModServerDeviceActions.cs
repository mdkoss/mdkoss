using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Unified action handlers for <see cref="ModServerDevice"/>.</summary>
internal static class ModServerDeviceActions
{
    internal static DeviceActionResult Execute(
        ModServerDevice device,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "start" or "listen" or "open" =>
                ToResult(device.StartServer(), "start_failed", () => StatusPayload(device)),
            "stop" or "close" =>
                ToResult(device.StopServer(), "stop_failed", () => StatusPayload(device)),
            "status" => DeviceActionResult.Ok(StatusPayload(device)),
            "readholding" or "read_holding" => ReadHolding(device, parameters),
            "writeholding" or "write_holding" => WriteHolding(device, parameters),
            "readinput" or "read_input" => ReadInput(device, parameters),
            "writeinput" or "write_input" => WriteInput(device, parameters),
            "readcoils" or "read_coils" => ReadCoils(device, parameters),
            "writecoils" or "write_coils" => WriteCoils(device, parameters),
            "readdiscrete" or "read_discrete" => ReadDiscrete(device, parameters),
            "writediscrete" or "write_discrete" => WriteDiscrete(device, parameters),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static object StatusPayload(ModServerDevice device) => new
    {
        device.Id,
        isListening = device.IsListening,
        bindAddress = device.Parameters.BindAddress,
        port = device.Parameters.Port,
        unitId = device.Parameters.UnitId,
        autoStart = device.Parameters.AutoStart,
        lastError = device.LastError,
    };

    private static DeviceActionResult ReadHolding(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadHoldingRegisters(address, count);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteHolding(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressUshorts(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteHoldingRegisters(address, values!);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadInput(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadInputRegisters(address, count);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteInput(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressUshorts(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteInputRegisters(address, values!);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadCoils(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadCoils(address, count);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteCoils(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressBools(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteCoils(address, values!);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ReadDiscrete(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressCount(parameters, out var address, out var count, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var (code, values) = device.ReadDiscreteInputs(address, count);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count, values })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult WriteDiscrete(ModServerDevice device, Dictionary<string, JsonElement>? parameters)
    {
        if (!TryGetAddressBools(parameters, out var address, out var values, out var error))
        {
            return DeviceActionResult.Fail(error!);
        }

        var code = device.WriteDiscreteInputs(address, values!);
        return code == ModServerErrorCode.Ok
            ? DeviceActionResult.Ok(new { address, count = values!.Length })
            : DeviceActionResult.Fail(code.ToString());
    }

    private static DeviceActionResult ToResult(
        ModServerErrorCode code,
        string failToken,
        Func<object> okPayload)
    {
        return code == ModServerErrorCode.Ok
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

        if (parameters is null
            || !TryGetUInt16(parameters, "address", out address))
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

        if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt16(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) && i is >= 0 and <= ushort.MaxValue)
        {
            value = (ushort)i;
            return true;
        }

        if (el.ValueKind == JsonValueKind.String
            && ushort.TryParse(el.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}
