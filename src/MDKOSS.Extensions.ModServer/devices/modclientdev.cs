using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using NModbus;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Modbus TCP client error / status codes.</summary>
public enum ModClientErrorCode
{
    Ok = 0,
    AlreadyConnected,
    NotConnected,
    InvalidParameter,
    Timeout,
    ConnectionFailed,
    OperationFailed,
}

/// <summary>Modbus data area for client read/write.</summary>
public enum ModClientArea
{
    Holding,
    Input,
    Coils,
    Discrete,
}

/// <summary>One item in a batch Modbus read.</summary>
public sealed class ModClientReadItem
{
    public ModClientArea Area { get; init; } = ModClientArea.Holding;

    public ushort Address { get; init; }

    public ushort Count { get; init; } = 1;

    /// <summary>Optional caller tag echoed in the result.</summary>
    public string? Tag { get; init; }
}

/// <summary>Result of one batch / single Modbus read.</summary>
public sealed class ModClientReadResult
{
    public string? Tag { get; init; }

    public ModClientArea Area { get; init; }

    public ushort Address { get; init; }

    public ushort Count { get; init; }

    public ushort[]? Registers { get; init; }

    public bool[]? Bits { get; init; }

    public ModClientErrorCode Error { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Modbus TCP client / master device (config type <c>devmodclient</c>).
/// Connects to a remote slave and reads coils / discrete / holding / input registers.
/// Contiguous large reads are auto-chunked; <see cref="ReadBatch"/> issues multiple ranges in one call.
/// </summary>
public sealed class ModClientDevice : MDeviceBase
{
    /// <summary>Modbus max registers per FC03/FC04 request.</summary>
    public const ushort MaxRegistersPerRequest = 125;

    /// <summary>Modbus max bits per FC01/FC02 request.</summary>
    public const ushort MaxBitsPerRequest = 2000;

    private readonly object _sync = new();
    private ModClientDeviceParameters _parameters;
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private string? _lastError;

    public ModClientDevice(string id, string name, ModClientDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new ModClientLogicalDriver(), vars)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        PublishStatusVars();
    }

    public ModClientDeviceParameters Parameters
    {
        get { lock (_sync) return _parameters; }
    }

    public bool IsConnected
    {
        get { lock (_sync) return IsConnectedUnlocked; }
    }

    private bool IsConnectedUnlocked =>
        _tcp is { Connected: true } && _master is not null;

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    /// <summary>Opens TCP + Modbus master to the remote slave.</summary>
    public ModClientErrorCode Connect(ModClientDeviceParameters? overrideParameters = null)
    {
        lock (_sync)
        {
            if (IsConnectedUnlocked)
            {
                return ModClientErrorCode.AlreadyConnected;
            }

            if (overrideParameters is not null)
            {
                _parameters = overrideParameters;
            }

            try
            {
                CleanupUnlocked();

                var tcp = new TcpClient();
                var result = tcp.BeginConnect(_parameters.Host, _parameters.Port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(_parameters.ConnectTimeoutMs))
                {
                    try { tcp.Close(); } catch { /* ignore */ }
                    _lastError = "connect_timeout";
                    State = MDeviceState.Fault;
                    PublishStatusVarsUnlocked();
                    return ModClientErrorCode.Timeout;
                }

                tcp.EndConnect(result);
                tcp.ReceiveTimeout = _parameters.ReadTimeoutMs;
                tcp.SendTimeout = _parameters.WriteTimeoutMs;

                var factory = new ModbusFactory();
                var master = factory.CreateMaster(tcp);
                master.Transport.ReadTimeout = _parameters.ReadTimeoutMs;
                master.Transport.WriteTimeout = _parameters.WriteTimeoutMs;

                _tcp = tcp;
                _master = master;
                _lastError = null;
                State = MDeviceState.Running;
                PublishStatusVarsUnlocked();
                return ModClientErrorCode.Ok;
            }
            catch (SocketException ex)
            {
                CleanupUnlocked();
                _lastError = ex.Message;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return ModClientErrorCode.ConnectionFailed;
            }
            catch (Exception ex)
            {
                CleanupUnlocked();
                _lastError = ex.Message;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return ModClientErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Closes the Modbus TCP connection.</summary>
    public ModClientErrorCode Disconnect()
    {
        lock (_sync)
        {
            if (_tcp is null && _master is null)
            {
                return ModClientErrorCode.NotConnected;
            }

            try
            {
                CleanupUnlocked();
                State = MDeviceState.Stopped;
                PublishStatusVarsUnlocked();
                return ModClientErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return ModClientErrorCode.OperationFailed;
            }
        }
    }

    public ModClientErrorCode SetParameters(ModClientDeviceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_sync)
        {
            if (IsConnectedUnlocked)
            {
                return ModClientErrorCode.AlreadyConnected;
            }

            _parameters = parameters;
            PublishStatusVarsUnlocked();
            return ModClientErrorCode.Ok;
        }
    }

    public (ModClientErrorCode error, ushort[]? values) ReadHoldingRegisters(ushort address, ushort count)
        => ReadRegisters(ModClientArea.Holding, address, count);

    public (ModClientErrorCode error, ushort[]? values) ReadInputRegisters(ushort address, ushort count)
        => ReadRegisters(ModClientArea.Input, address, count);

    public (ModClientErrorCode error, bool[]? values) ReadCoils(ushort address, ushort count)
        => ReadBits(ModClientArea.Coils, address, count);

    public (ModClientErrorCode error, bool[]? values) ReadDiscreteInputs(ushort address, ushort count)
        => ReadBits(ModClientArea.Discrete, address, count);

    public ModClientErrorCode WriteHoldingRegisters(ushort address, ushort[] values)
    {
        if (values is null || values.Length == 0)
        {
            return ModClientErrorCode.InvalidParameter;
        }

        lock (_sync)
        {
            if (!IsConnectedUnlocked)
            {
                return ModClientErrorCode.NotConnected;
            }

            try
            {
                var unitId = _parameters.UnitId;
                var master = _master!;
                var offset = 0;
                var start = address;
                while (offset < values.Length)
                {
                    var chunk = (ushort)Math.Min(MaxRegistersPerRequest, values.Length - offset);
                    var slice = new ushort[chunk];
                    Array.Copy(values, offset, slice, 0, chunk);
                    if (chunk == 1)
                    {
                        master.WriteSingleRegister(unitId, start, slice[0]);
                    }
                    else
                    {
                        master.WriteMultipleRegisters(unitId, start, slice);
                    }

                    offset += chunk;
                    start = (ushort)(start + chunk);
                }

                Vars.Set(BuildVarKey("lastWriteUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastWriteArea"), "holding");
                Vars.Set(BuildVarKey("lastWriteAddress"), address);
                Vars.Set(BuildVarKey("lastWriteCount"), values.Length);
                return ModClientErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return MapException(ex);
            }
        }
    }

    public ModClientErrorCode WriteCoils(ushort address, bool[] values)
    {
        if (values is null || values.Length == 0)
        {
            return ModClientErrorCode.InvalidParameter;
        }

        lock (_sync)
        {
            if (!IsConnectedUnlocked)
            {
                return ModClientErrorCode.NotConnected;
            }

            try
            {
                var unitId = _parameters.UnitId;
                var master = _master!;
                var offset = 0;
                var start = address;
                while (offset < values.Length)
                {
                    var chunk = (ushort)Math.Min(MaxBitsPerRequest, values.Length - offset);
                    var slice = new bool[chunk];
                    Array.Copy(values, offset, slice, 0, chunk);
                    if (chunk == 1)
                    {
                        master.WriteSingleCoil(unitId, start, slice[0]);
                    }
                    else
                    {
                        master.WriteMultipleCoils(unitId, start, slice);
                    }

                    offset += chunk;
                    start = (ushort)(start + chunk);
                }

                Vars.Set(BuildVarKey("lastWriteUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastWriteArea"), "coils");
                Vars.Set(BuildVarKey("lastWriteAddress"), address);
                Vars.Set(BuildVarKey("lastWriteCount"), values.Length);
                return ModClientErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return MapException(ex);
            }
        }
    }

    /// <summary>
    /// Batch-read multiple ranges on the current connection.
    /// Continues after per-item failures; each result carries its own <see cref="ModClientReadResult.Error"/>.
    /// </summary>
    public IReadOnlyList<ModClientReadResult> ReadBatch(IReadOnlyList<ModClientReadItem> items)
    {
        if (items is null || items.Count == 0)
        {
            return Array.Empty<ModClientReadResult>();
        }

        var results = new List<ModClientReadResult>(items.Count);
        foreach (var item in items)
        {
            results.Add(ReadOne(item));
        }

        lock (_sync)
        {
            Vars.Set(BuildVarKey("lastBatchUtc"), DateTime.UtcNow);
            Vars.Set(BuildVarKey("lastBatchCount"), results.Count);
            Vars.Set(BuildVarKey("lastBatchOk"), results.Count(r => r.Error == ModClientErrorCode.Ok));
        }

        return results;
    }

    public override void Start()
    {
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();

        if (Parameters.AutoConnect)
        {
            Connect();
        }
    }

    public override void Stop()
    {
        Disconnect();
        base.Stop();
    }

    public override void Dispose()
    {
        Disconnect();
        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "devmodclient",
                State.ToString(),
                $"modbus-tcp-client:{_parameters.Host}:{_parameters.Port}",
                IsConnectedUnlocked);
        }
    }

    private ModClientReadResult ReadOne(ModClientReadItem item)
    {
        if (item.Count == 0)
        {
            return FailResult(item, ModClientErrorCode.InvalidParameter, "invalid_count");
        }

        return item.Area switch
        {
            ModClientArea.Holding or ModClientArea.Input =>
                ToRegisterResult(item, ReadRegisters(item.Area, item.Address, item.Count)),
            ModClientArea.Coils or ModClientArea.Discrete =>
                ToBitResult(item, ReadBits(item.Area, item.Address, item.Count)),
            _ => FailResult(item, ModClientErrorCode.InvalidParameter, "invalid_area"),
        };
    }

    private static ModClientReadResult ToRegisterResult(
        ModClientReadItem item,
        (ModClientErrorCode error, ushort[]? values) read)
    {
        return new ModClientReadResult
        {
            Tag = item.Tag,
            Area = item.Area,
            Address = item.Address,
            Count = item.Count,
            Registers = read.values,
            Error = read.error,
            ErrorMessage = read.error == ModClientErrorCode.Ok ? null : read.error.ToString(),
        };
    }

    private static ModClientReadResult ToBitResult(
        ModClientReadItem item,
        (ModClientErrorCode error, bool[]? values) read)
    {
        return new ModClientReadResult
        {
            Tag = item.Tag,
            Area = item.Area,
            Address = item.Address,
            Count = item.Count,
            Bits = read.values,
            Error = read.error,
            ErrorMessage = read.error == ModClientErrorCode.Ok ? null : read.error.ToString(),
        };
    }

    private static ModClientReadResult FailResult(ModClientReadItem item, ModClientErrorCode error, string message)
        => new()
        {
            Tag = item.Tag,
            Area = item.Area,
            Address = item.Address,
            Count = item.Count,
            Error = error,
            ErrorMessage = message,
        };

    private (ModClientErrorCode error, ushort[]? values) ReadRegisters(
        ModClientArea area,
        ushort address,
        ushort count)
    {
        if (count == 0)
        {
            return (ModClientErrorCode.InvalidParameter, null);
        }

        if (area is not (ModClientArea.Holding or ModClientArea.Input))
        {
            return (ModClientErrorCode.InvalidParameter, null);
        }

        lock (_sync)
        {
            if (!IsConnectedUnlocked)
            {
                return (ModClientErrorCode.NotConnected, null);
            }

            try
            {
                var unitId = _parameters.UnitId;
                var master = _master!;
                var buffer = new ushort[count];
                var offset = 0;
                var start = address;
                while (offset < count)
                {
                    var chunk = (ushort)Math.Min(MaxRegistersPerRequest, count - offset);
                    var part = area == ModClientArea.Holding
                        ? master.ReadHoldingRegisters(unitId, start, chunk)
                        : master.ReadInputRegisters(unitId, start, chunk);
                    Array.Copy(part, 0, buffer, offset, chunk);
                    offset += chunk;
                    start = (ushort)(start + chunk);
                }

                Vars.Set(BuildVarKey("lastReadUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastReadArea"), area.ToString().ToLowerInvariant());
                Vars.Set(BuildVarKey("lastReadAddress"), address);
                Vars.Set(BuildVarKey("lastReadCount"), count);
                return (ModClientErrorCode.Ok, buffer);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return (MapException(ex), null);
            }
        }
    }

    private (ModClientErrorCode error, bool[]? values) ReadBits(
        ModClientArea area,
        ushort address,
        ushort count)
    {
        if (count == 0)
        {
            return (ModClientErrorCode.InvalidParameter, null);
        }

        if (area is not (ModClientArea.Coils or ModClientArea.Discrete))
        {
            return (ModClientErrorCode.InvalidParameter, null);
        }

        lock (_sync)
        {
            if (!IsConnectedUnlocked)
            {
                return (ModClientErrorCode.NotConnected, null);
            }

            try
            {
                var unitId = _parameters.UnitId;
                var master = _master!;
                var buffer = new bool[count];
                var offset = 0;
                var start = address;
                while (offset < count)
                {
                    var chunk = (ushort)Math.Min(MaxBitsPerRequest, count - offset);
                    var part = area == ModClientArea.Coils
                        ? master.ReadCoils(unitId, start, chunk)
                        : master.ReadInputs(unitId, start, chunk);
                    Array.Copy(part, 0, buffer, offset, chunk);
                    offset += chunk;
                    start = (ushort)(start + chunk);
                }

                Vars.Set(BuildVarKey("lastReadUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastReadArea"), area.ToString().ToLowerInvariant());
                Vars.Set(BuildVarKey("lastReadAddress"), address);
                Vars.Set(BuildVarKey("lastReadCount"), count);
                return (ModClientErrorCode.Ok, buffer);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return (MapException(ex), null);
            }
        }
    }

    private static ModClientErrorCode MapException(Exception ex)
    {
        if (ex is TimeoutException or IOException { InnerException: SocketException })
        {
            return ModClientErrorCode.Timeout;
        }

        if (ex is SocketException or IOException)
        {
            return ModClientErrorCode.ConnectionFailed;
        }

        return ModClientErrorCode.OperationFailed;
    }

    private void CleanupUnlocked()
    {
        try
        {
            _master?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _tcp?.Close();
        }
        catch
        {
            // ignore
        }

        _master = null;
        _tcp = null;
    }

    private void PublishStatusVars()
    {
        lock (_sync)
        {
            PublishStatusVarsUnlocked();
        }
    }

    private void PublishStatusVarsUnlocked()
    {
        Vars.Set(BuildVarKey("isConnected"), IsConnectedUnlocked);
        Vars.Set(BuildVarKey("host"), _parameters.Host);
        Vars.Set(BuildVarKey("port"), _parameters.Port);
        Vars.Set(BuildVarKey("unitId"), _parameters.UnitId);
        Vars.Set(BuildVarKey("autoConnect"), _parameters.AutoConnect);
        if (_lastError is not null)
        {
            Vars.Set(BuildVarKey("lastError"), _lastError);
        }

        WriteState(State.ToString().ToLowerInvariant());
    }
}

/// <summary>Minimal IDriver stub — Modbus I/O lives on the device, not a motion card.</summary>
internal sealed class ModClientLogicalDriver : IDriver
{
    public string Name => "MODCLIENT";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config) { }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value) => false;

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        return false;
    }

    public bool WriteDo(short doType, int value) => false;

    public bool WriteDoBit(short doType, short doIndex, bool value) => false;

    public bool EnableAxis(short axis) => false;

    public bool DisableAxis(short axis) => false;

    public bool IsAxisEnabled(short axis) => false;

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        return false;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        return false;
    }

    public bool SetAxisPosition(short axis, double position) => false;

    public bool SetAxisVelocity(short axis, double velocity) => false;

    public bool SetAxisAcceleration(short axis, double acceleration) => false;

    public bool SetAxisDeceleration(short axis, double deceleration) => false;

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose() { }
}
