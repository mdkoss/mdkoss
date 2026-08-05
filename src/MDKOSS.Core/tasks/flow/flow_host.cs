using System.Text.Json;

namespace MDKOSS.Core.Flow;

/// <summary>Narrow host surface for flow ops (IO / device actions).</summary>
public interface IFlowRuntimeHost
{
    bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error);

    DeviceActionResult ExecuteDeviceAction(
        string deviceId,
        string action,
        Dictionary<string, JsonElement>? parameters);
}

/// <summary>No-op host for unit tests / offline validation.</summary>
public sealed class NullFlowRuntimeHost : IFlowRuntimeHost
{
    public static NullFlowRuntimeHost Instance { get; } = new();

    public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error)
    {
        error = "no_runtime_host";
        return false;
    }

    public DeviceActionResult ExecuteDeviceAction(
        string deviceId,
        string action,
        Dictionary<string, JsonElement>? parameters) =>
        DeviceActionResult.Fail("no_runtime_host");
}
