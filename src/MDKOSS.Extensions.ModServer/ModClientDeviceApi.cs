using MDKOSS.Core;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Modbus TCP client device runtime API for monitoring modules.</summary>
public static class ModClientDeviceApi
{
    public static object? GetStatus(MdkRuntime runtime, string deviceId)
    {
        if (!TryGet(runtime, deviceId, out var device))
        {
            return null;
        }

        return new
        {
            deviceId = device.Id,
            isConnected = device.IsConnected,
            host = device.Parameters.Host,
            port = device.Parameters.Port,
            unitId = device.Parameters.UnitId,
            autoConnect = device.Parameters.AutoConnect,
            lastError = device.LastError,
        };
    }

    public static ModClientErrorCode Connect(
        MdkRuntime runtime,
        string deviceId,
        ModClientDeviceParameters? config = null)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.Connect(config)
            : ModClientErrorCode.OperationFailed;
    }

    public static ModClientErrorCode Disconnect(MdkRuntime runtime, string deviceId)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.Disconnect()
            : ModClientErrorCode.NotConnected;
    }

    public static (ModClientErrorCode error, ushort[]? values) ReadHolding(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadHoldingRegisters(address, count)
            : (ModClientErrorCode.OperationFailed, null);
    }

    public static ModClientErrorCode WriteHolding(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteHoldingRegisters(address, values)
            : ModClientErrorCode.OperationFailed;
    }

    public static (ModClientErrorCode error, ushort[]? values) ReadInput(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadInputRegisters(address, count)
            : (ModClientErrorCode.OperationFailed, null);
    }

    public static (ModClientErrorCode error, bool[]? values) ReadCoils(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadCoils(address, count)
            : (ModClientErrorCode.OperationFailed, null);
    }

    public static ModClientErrorCode WriteCoils(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        bool[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteCoils(address, values)
            : ModClientErrorCode.OperationFailed;
    }

    public static (ModClientErrorCode error, bool[]? values) ReadDiscrete(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadDiscreteInputs(address, count)
            : (ModClientErrorCode.OperationFailed, null);
    }

    public static IReadOnlyList<ModClientReadResult>? ReadBatch(
        MdkRuntime runtime,
        string deviceId,
        IReadOnlyList<ModClientReadItem> items)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadBatch(items)
            : null;
    }

    private static bool TryGet(MdkRuntime runtime, string deviceId, out ModClientDevice device)
    {
        device = null!;
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not ModClientDevice mod)
        {
            return false;
        }

        device = mod;
        return true;
    }
}
