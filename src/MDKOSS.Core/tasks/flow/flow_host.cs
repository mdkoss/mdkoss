using System.Text.Json;

namespace MDKOSS.Core.Flow;

/// <summary>
/// Host surface for flow ops — mirrors <c>MDKOSS.Tasks.MotionTask</c> helpers
/// (axis / platform / GPIO / snapshot / driver check) plus generic device actions.
/// </summary>
public interface IFlowRuntimeHost
{
    bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error);

    DeviceActionResult ExecuteDeviceAction(
        string deviceId,
        string action,
        Dictionary<string, JsonElement>? parameters);

    // ---- MotionTask-equivalent helpers ----

    bool TryAxisMoveTo(string axisDeviceId, double position, out string? error);

    bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error);

    bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error);

    bool TryPlatformAxisMoveTo(
        string platformDeviceId,
        string axisLetter,
        double position,
        out string? error);

    bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error);

    bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error);

    bool TryGetDeviceSnapshot(
        string deviceId,
        out string? deviceType,
        out string? state,
        out bool driverConnected,
        out string? error);

    /// <summary>Fails when the driver bound to <paramref name="deviceId"/> is disconnected.</summary>
    bool TryEnsureDriverConnected(string deviceId, out string? error);
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

    public bool TryAxisMoveTo(string axisDeviceId, double position, out string? error)
    {
        error = "no_runtime_host";
        return false;
    }

    public bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error)
    {
        error = "no_runtime_host";
        return false;
    }

    public bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error)
    {
        error = "no_runtime_host";
        return false;
    }

    public bool TryPlatformAxisMoveTo(
        string platformDeviceId,
        string axisLetter,
        double position,
        out string? error)
    {
        error = "no_runtime_host";
        return false;
    }

    public bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error) =>
        TryWriteDigitalOutput(gpioDeviceId, alias, value, out error);

    public bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error)
    {
        value = false;
        error = "no_runtime_host";
        return false;
    }

    public bool TryGetDeviceSnapshot(
        string deviceId,
        out string? deviceType,
        out string? state,
        out bool driverConnected,
        out string? error)
    {
        deviceType = null;
        state = null;
        driverConnected = false;
        error = "no_runtime_host";
        return false;
    }

    public bool TryEnsureDriverConnected(string deviceId, out string? error)
    {
        error = "no_runtime_host";
        return false;
    }
}
