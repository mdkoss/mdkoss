using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using NModbus;
using NModbus.Data;

namespace MDKOSS.Extensions.ModServer;

/// <summary>Modbus TCP server error / status codes.</summary>
public enum ModServerErrorCode
{
    Ok = 0,
    AlreadyListening,
    NotListening,
    InvalidParameter,
    InvalidAddress,
    OperationFailed,
}

/// <summary>
/// Modbus TCP server device (config type <c>devmodserver</c>).
/// Exposes coils / discrete inputs / holding / input registers to external Modbus masters.
/// Local tasks and monitoring can read/write the same data store.
/// </summary>
public sealed class ModServerDevice : MDeviceBase
{
    private readonly object _sync = new();
    private ModServerDeviceParameters _parameters;
    private DefaultSlaveDataStore? _dataStore;
    private TcpListener? _listener;
    private IModbusSlaveNetwork? _network;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private string? _lastError;

    public ModServerDevice(string id, string name, ModServerDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new ModServerLogicalDriver(), vars)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _dataStore = new DefaultSlaveDataStore();
        PublishStatusVars();
    }

    public ModServerDeviceParameters Parameters
    {
        get { lock (_sync) return _parameters; }
    }

    public bool IsListening
    {
        get { lock (_sync) return IsListeningUnlocked; }
    }

    private bool IsListeningUnlocked => _listenTask is { IsCompleted: false };

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    /// <summary>Starts Modbus TCP listen (slave network).</summary>
    public ModServerErrorCode StartServer(ModServerDeviceParameters? overrideParameters = null)
    {
        lock (_sync)
        {
            if (_listenTask is { IsCompleted: false })
            {
                return ModServerErrorCode.AlreadyListening;
            }

            if (overrideParameters is not null)
            {
                _parameters = overrideParameters;
            }

            try
            {
                if (!IPAddress.TryParse(_parameters.BindAddress, out var bindAddress))
                {
                    _lastError = "invalid_bind_address";
                    PublishStatusVarsUnlocked();
                    return ModServerErrorCode.InvalidParameter;
                }

                _dataStore ??= new DefaultSlaveDataStore();
                var factory = new ModbusFactory();
                var slave = factory.CreateSlave(_parameters.UnitId, _dataStore);

                _listener = new TcpListener(bindAddress, _parameters.Port);
                _listener.Start();
                _network = factory.CreateSlaveNetwork(_listener);
                _network.AddSlave(slave);

                _listenCts = new CancellationTokenSource();
                var token = _listenCts.Token;
                var network = _network;
                _listenTask = Task.Run(async () =>
                {
                    try
                    {
                        await network.ListenAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected on StopServer
                    }
                    catch (Exception ex)
                    {
                        lock (_sync)
                        {
                            _lastError = ex.Message;
                            PublishStatusVarsUnlocked();
                        }
                    }
                }, CancellationToken.None);

                _lastError = null;
                State = MDeviceState.Running;
                PublishStatusVarsUnlocked();
                return ModServerErrorCode.Ok;
            }
            catch (Exception ex)
            {
                CleanupServerUnlocked();
                _lastError = ex.Message;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return ModServerErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Stops Modbus TCP listen and releases the listener.</summary>
    public ModServerErrorCode StopServer()
    {
        lock (_sync)
        {
            if (_listenTask is null && _listener is null)
            {
                return ModServerErrorCode.NotListening;
            }

            try
            {
                CleanupServerUnlocked();
                State = MDeviceState.Stopped;
                PublishStatusVarsUnlocked();
                return ModServerErrorCode.Ok;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatusVarsUnlocked();
                return ModServerErrorCode.OperationFailed;
            }
        }
    }

    public ModServerErrorCode SetParameters(ModServerDeviceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_sync)
        {
            if (_listenTask is { IsCompleted: false })
            {
                return ModServerErrorCode.AlreadyListening;
            }

            _parameters = parameters;
            PublishStatusVarsUnlocked();
            return ModServerErrorCode.Ok;
        }
    }

    public (ModServerErrorCode error, ushort[]? values) ReadHoldingRegisters(ushort address, ushort count)
        => ReadRegisters(store => store.HoldingRegisters, address, count);

    public ModServerErrorCode WriteHoldingRegisters(ushort address, ushort[] values)
        => WriteRegisters(store => store.HoldingRegisters, address, values);

    public (ModServerErrorCode error, ushort[]? values) ReadInputRegisters(ushort address, ushort count)
        => ReadRegisters(store => store.InputRegisters, address, count);

    public ModServerErrorCode WriteInputRegisters(ushort address, ushort[] values)
        => WriteRegisters(store => store.InputRegisters, address, values);

    public (ModServerErrorCode error, bool[]? values) ReadCoils(ushort address, ushort count)
        => ReadBits(store => store.CoilDiscretes, address, count);

    public ModServerErrorCode WriteCoils(ushort address, bool[] values)
        => WriteBits(store => store.CoilDiscretes, address, values);

    public (ModServerErrorCode error, bool[]? values) ReadDiscreteInputs(ushort address, ushort count)
        => ReadBits(store => store.CoilInputs, address, count);

    public ModServerErrorCode WriteDiscreteInputs(ushort address, bool[] values)
        => WriteBits(store => store.CoilInputs, address, values);

    public override void Start()
    {
        // Do not call EnsureConnected — listen state is managed by StartServer/StopServer.
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();

        if (Parameters.AutoStart)
        {
            StartServer();
        }
    }

    public override void Stop()
    {
        StopServer();
        base.Stop();
    }

    public override void Dispose()
    {
        StopServer();
        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "devmodserver",
                State.ToString(),
                $"modbus-tcp:{_parameters.BindAddress}:{_parameters.Port}",
                IsListeningUnlocked);
        }
    }

    private (ModServerErrorCode error, ushort[]? values) ReadRegisters(
        Func<DefaultSlaveDataStore, IPointSource<ushort>> selector,
        ushort address,
        ushort count)
    {
        if (count == 0)
        {
            return (ModServerErrorCode.InvalidParameter, null);
        }

        lock (_sync)
        {
            try
            {
                EnsureDataStoreUnlocked();
                var values = selector(_dataStore!).ReadPoints(address, count);
                return (ModServerErrorCode.Ok, values);
            }
            catch (ArgumentException)
            {
                return (ModServerErrorCode.InvalidAddress, null);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return (ModServerErrorCode.OperationFailed, null);
            }
        }
    }

    private ModServerErrorCode WriteRegisters(
        Func<DefaultSlaveDataStore, IPointSource<ushort>> selector,
        ushort address,
        ushort[] values)
    {
        if (values is null || values.Length == 0)
        {
            return ModServerErrorCode.InvalidParameter;
        }

        lock (_sync)
        {
            try
            {
                EnsureDataStoreUnlocked();
                selector(_dataStore!).WritePoints(address, values);
                Vars.Set(BuildVarKey("lastWriteUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastWriteKind"), "registers");
                Vars.Set(BuildVarKey("lastWriteAddress"), address);
                Vars.Set(BuildVarKey("lastWriteCount"), values.Length);
                return ModServerErrorCode.Ok;
            }
            catch (ArgumentException)
            {
                return ModServerErrorCode.InvalidAddress;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return ModServerErrorCode.OperationFailed;
            }
        }
    }

    private (ModServerErrorCode error, bool[]? values) ReadBits(
        Func<DefaultSlaveDataStore, IPointSource<bool>> selector,
        ushort address,
        ushort count)
    {
        if (count == 0)
        {
            return (ModServerErrorCode.InvalidParameter, null);
        }

        lock (_sync)
        {
            try
            {
                EnsureDataStoreUnlocked();
                var values = selector(_dataStore!).ReadPoints(address, count);
                return (ModServerErrorCode.Ok, values);
            }
            catch (ArgumentException)
            {
                return (ModServerErrorCode.InvalidAddress, null);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return (ModServerErrorCode.OperationFailed, null);
            }
        }
    }

    private ModServerErrorCode WriteBits(
        Func<DefaultSlaveDataStore, IPointSource<bool>> selector,
        ushort address,
        bool[] values)
    {
        if (values is null || values.Length == 0)
        {
            return ModServerErrorCode.InvalidParameter;
        }

        lock (_sync)
        {
            try
            {
                EnsureDataStoreUnlocked();
                selector(_dataStore!).WritePoints(address, values);
                Vars.Set(BuildVarKey("lastWriteUtc"), DateTime.UtcNow);
                Vars.Set(BuildVarKey("lastWriteKind"), "bits");
                Vars.Set(BuildVarKey("lastWriteAddress"), address);
                Vars.Set(BuildVarKey("lastWriteCount"), values.Length);
                return ModServerErrorCode.Ok;
            }
            catch (ArgumentException)
            {
                return ModServerErrorCode.InvalidAddress;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return ModServerErrorCode.OperationFailed;
            }
        }
    }

    private void EnsureDataStoreUnlocked()
    {
        _dataStore ??= new DefaultSlaveDataStore();
    }

    private void CleanupServerUnlocked()
    {
        try
        {
            _listenCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }

        try
        {
            _network?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _listenCts?.Dispose();
        _listenCts = null;
        _listenTask = null;
        _network = null;
        _listener = null;
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
        Vars.Set(BuildVarKey("isListening"), IsListeningUnlocked);
        Vars.Set(BuildVarKey("bindAddress"), _parameters.BindAddress);
        Vars.Set(BuildVarKey("port"), _parameters.Port);
        Vars.Set(BuildVarKey("unitId"), _parameters.UnitId);
        Vars.Set(BuildVarKey("autoStart"), _parameters.AutoStart);
        if (_lastError is not null)
        {
            Vars.Set(BuildVarKey("lastError"), _lastError);
        }

        WriteState(State.ToString().ToLowerInvariant());
    }
}

/// <summary>Minimal IDriver stub — Modbus I/O lives on the device, not a motion card.</summary>
internal sealed class ModServerLogicalDriver : IDriver
{
    public string Name => "MODSERVER";

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
