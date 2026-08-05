using MDKOSS.Core;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Modbus TCP server device runtime API for monitoring modules.</summary>
public static class ModServerDeviceApi
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
            isListening = device.IsListening,
            bindAddress = device.Parameters.BindAddress,
            port = device.Parameters.Port,
            unitId = device.Parameters.UnitId,
            autoStart = device.Parameters.AutoStart,
            lastError = device.LastError,
        };
    }

    public static ModServerErrorCode StartServer(
        MdkRuntime runtime,
        string deviceId,
        ModServerDeviceParameters? config = null)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.StartServer(config)
            : ModServerErrorCode.OperationFailed;
    }

    public static ModServerErrorCode StopServer(MdkRuntime runtime, string deviceId)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.StopServer()
            : ModServerErrorCode.NotListening;
    }

    public static (ModServerErrorCode error, ushort[]? values) ReadHolding(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadHoldingRegisters(address, count)
            : (ModServerErrorCode.OperationFailed, null);
    }

    public static ModServerErrorCode WriteHolding(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteHoldingRegisters(address, values)
            : ModServerErrorCode.OperationFailed;
    }

    public static (ModServerErrorCode error, ushort[]? values) ReadInput(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadInputRegisters(address, count)
            : (ModServerErrorCode.OperationFailed, null);
    }

    public static ModServerErrorCode WriteInput(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteInputRegisters(address, values)
            : ModServerErrorCode.OperationFailed;
    }

    public static (ModServerErrorCode error, bool[]? values) ReadCoils(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadCoils(address, count)
            : (ModServerErrorCode.OperationFailed, null);
    }

    public static ModServerErrorCode WriteCoils(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        bool[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteCoils(address, values)
            : ModServerErrorCode.OperationFailed;
    }

    public static (ModServerErrorCode error, bool[]? values) ReadDiscrete(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        ushort count)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.ReadDiscreteInputs(address, count)
            : (ModServerErrorCode.OperationFailed, null);
    }

    public static ModServerErrorCode WriteDiscrete(
        MdkRuntime runtime,
        string deviceId,
        ushort address,
        bool[] values)
    {
        return TryGet(runtime, deviceId, out var device)
            ? device.WriteDiscreteInputs(address, values)
            : ModServerErrorCode.OperationFailed;
    }

    private static bool TryGet(MdkRuntime runtime, string deviceId, out ModServerDevice device)
    {
        device = null!;
        if (!runtime.TryGetDevice(deviceId, out var dev) || dev is not ModServerDevice mod)
        {
            return false;
        }

        device = mod;
        return true;
    }
}
