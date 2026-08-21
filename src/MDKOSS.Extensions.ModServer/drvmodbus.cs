using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using NModbus;

namespace MDKOSS.Extensions.ModServer;

/// <summary>
/// Modbus TCP master <see cref="IDriver"/> (NModbus).
/// Digital IO uses the same <see cref="DriverIoAddress"/> form as SIM/DMC/S7
/// (<c>di.gpi.bit.n</c> / <c>do.gpo.bit.n</c>, 0-based by default).
/// DI maps to discrete inputs (or coils/holding via parameters); DO maps to coils (or holding).
/// When <c>simulate=true</c> or <c>host</c> is empty, IO is kept in memory (no slave required).
/// Axis / interpolation APIs return false — this driver is an IO backend, not a motion card.
/// </summary>
public sealed class DrvModbus : IDriver
{
    private const int PortBitWidth = 32;

    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, bool> _simDiscrete = new();
    private readonly ConcurrentDictionary<int, bool> _simCoils = new();
    private readonly ConcurrentDictionary<int, ushort> _simHolding = new();
    private readonly ConcurrentDictionary<int, ushort> _simInputRegs = new();
    private readonly DriverIoPortCache _ioCache = new();
    private readonly object _gate = new();
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private bool _simulate;
    private byte _unitId = 1;
    private short _ioBitBase;
    private ushort _diAddress;
    private ushort _doAddress;
    private DiSource _diSource = DiSource.Discrete;
    private DoTarget _doTarget = DoTarget.Coils;
    private int _disposed;

    public string Name => "MODBUS";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _ioBitBase = ParseIoBitBase(config);
        _diAddress = (ushort)Math.Clamp(GetInt(config, "diAddress", 0), 0, ushort.MaxValue);
        _doAddress = (ushort)Math.Clamp(GetInt(config, "doAddress", 0), 0, ushort.MaxValue);
        _unitId = (byte)Math.Clamp(GetInt(config, "unitId", 1), 0, 255);
        _diSource = ParseDiSource(GetString(config, "diArea", "discrete"));
        _doTarget = ParseDoTarget(GetString(config, "doArea", "coils"));
        var host = GetString(config, "host", string.Empty).Trim();
        var port = Math.Clamp(GetInt(config, "port", 502), 1, 65535);
        var connectTimeoutMs = Math.Max(100, GetInt(config, "connectTimeoutMs", 3000));
        var readTimeoutMs = Math.Max(100, GetInt(config, "readTimeoutMs", 3000));
        var writeTimeoutMs = Math.Max(100, GetInt(config, "writeTimeoutMs", 3000));
        var forceSimulate = GetBool(config, "simulate", host.Length == 0);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.host"] = host;
        _memory["driver.port"] = port;
        _memory["driver.unitId"] = _unitId;
        _memory["driver.diAddress"] = _diAddress;
        _memory["driver.doAddress"] = _doAddress;
        _memory["driver.diArea"] = _diSource.ToString();
        _memory["driver.doArea"] = _doTarget.ToString();
        _memory["driver.ioBitBase"] = _ioBitBase;
        _memory["driver.lastCode"] = 0;

        CloseMasterUnlocked();
        _simDiscrete.Clear();
        _simCoils.Clear();
        _simHolding.Clear();
        _simInputRegs.Clear();
        _ioCache.Clear();

        if (forceSimulate)
        {
            _simulate = true;
            _memory["driver.mode"] = "simulation";
            IsConnected = true;
            return;
        }

        _simulate = false;
        _memory["driver.mode"] = "live";
        try
        {
            var tcp = new TcpClient();
            var result = tcp.BeginConnect(host, port, null, null);
            if (!result.AsyncWaitHandle.WaitOne(connectTimeoutMs))
            {
                try { tcp.Close(); } catch { /* ignore */ }
                _memory["driver.lastError"] = "connect_timeout";
                IsConnected = false;
                return;
            }

            tcp.EndConnect(result);
            tcp.ReceiveTimeout = readTimeoutMs;
            tcp.SendTimeout = writeTimeoutMs;

            var factory = new ModbusFactory();
            var master = factory.CreateMaster(tcp);
            master.Transport.ReadTimeout = readTimeoutMs;
            master.Transport.WriteTimeout = writeTimeoutMs;

            _tcp = tcp;
            _master = master;
            IsConnected = true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            CloseMasterUnlocked();
            IsConnected = false;
        }
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (TryReadIoAddress(address, out value))
        {
            return true;
        }

        if (TryReadNativeAddress(address, out value))
        {
            return true;
        }

        if (DriverIoAddress.LooksLike(address))
        {
            return false;
        }

        return _memory.TryGetValue(address, out value);
    }

    public bool Write(string address, object? value)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (TryWriteIoAddress(address, value))
        {
            _memory[address] = value;
            return true;
        }

        if (TryWriteNativeAddress(address, value))
        {
            _memory[address] = value;
            return true;
        }

        if (DriverIoAddress.LooksLike(address))
        {
            return false;
        }

        _memory[address] = value;
        return true;
    }

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        if (_ioCache.TryGet(false, diType, out value))
        {
            return true;
        }

        lock (_gate)
        {
            if (_ioCache.TryGet(false, diType, out value))
            {
                return true;
            }

            if (!TryReadPortWordUnlocked(isOutput: false, diType, out value))
            {
                return false;
            }

            _ioCache.Set(false, diType, value);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        if (_ioCache.TryGet(true, doType, out value))
        {
            return true;
        }

        lock (_gate)
        {
            if (_ioCache.TryGet(true, doType, out value))
            {
                return true;
            }

            if (!TryReadPortWordUnlocked(isOutput: true, doType, out value))
            {
                return false;
            }

            _ioCache.Set(true, doType, value);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

    public bool WriteDo(short doType, int value)
    {
        if (!IsConnected)
        {
            return false;
        }

        lock (_gate)
        {
            if (!TryWritePortWordUnlocked(doType, value))
            {
                return false;
            }

            _ioCache.Invalidate(true, doType);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        if (!IsConnected || doIndex < 0 || doIndex > 31)
        {
            return false;
        }

        lock (_gate)
        {
            if (!TryReadPortWordUnlocked(isOutput: true, doType, out var current))
            {
                return false;
            }

            var next = ApplyPortBit(current, doIndex, value);
            if (!TryWritePortWordUnlocked(doType, next))
            {
                return false;
            }

            _ioCache.Invalidate(true, doType);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            IsConnected = false;
            CloseMasterUnlocked();
            _ioCache.Clear();
        }
    }

    private bool TryReadIoAddress(string address, out object? value)
    {
        value = null;
        if (!DriverIoAddress.TryParse(address, out var io))
        {
            return false;
        }

        if (io.IsOutput)
        {
            if (!TryReadDo(io.Type, out var doValue))
            {
                return false;
            }

            if (io.IsBit)
            {
                if (!TryAddressBitShift(io.BitIndex!.Value, out var shift))
                {
                    return false;
                }

                value = TestPortBit(doValue, shift);
                return true;
            }

            value = doValue;
            return true;
        }

        if (!TryReadDi(io.Type, out var diValue))
        {
            return false;
        }

        if (io.IsBit)
        {
            if (!TryAddressBitShift(io.BitIndex!.Value, out var shift))
            {
                return false;
            }

            value = TestPortBit(diValue, shift);
            return true;
        }

        value = diValue;
        return true;
    }

    private bool TryWriteIoAddress(string address, object? value)
    {
        if (!DriverIoAddress.TryParse(address, out var io))
        {
            return false;
        }

        if (io.IsBit)
        {
            if (!TryAddressBitShift(io.BitIndex!.Value, out var shift))
            {
                return false;
            }

            var bit = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            return io.IsOutput
                ? WriteDoBit(io.Type, (short)shift, bit)
                : WriteDiBit(io.Type, (short)shift, bit);
        }

        if (value is bool || !TryConvertToInt(value, out var word))
        {
            return false;
        }

        return io.IsOutput ? WriteDo(io.Type, word) : WriteDi(io.Type, word);
    }

    private bool WriteDi(short diType, int value)
    {
        if (!IsConnected || !_simulate)
        {
            return false;
        }

        lock (_gate)
        {
            if (!TryWritePortWordDiUnlocked(diType, value))
            {
                return false;
            }

            _ioCache.Invalidate(false, diType);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

    private bool WriteDiBit(short diType, short diIndex, bool value)
    {
        if (!IsConnected || !_simulate || diIndex < 0 || diIndex > 31)
        {
            return false;
        }

        lock (_gate)
        {
            if (!TryReadPortWordUnlocked(isOutput: false, diType, out var current))
            {
                return false;
            }

            var next = ApplyPortBit(current, diIndex, value);
            if (!TryWritePortWordDiUnlocked(diType, next))
            {
                return false;
            }

            _ioCache.Invalidate(false, diType);
            _memory["driver.lastCode"] = 0;
            return true;
        }
    }

    private bool TryWritePortWordDiUnlocked(short diType, int value)
    {
        var start = ResolveBitStart(isOutput: false, diType);
        if (_diSource == DiSource.Holding)
        {
            return TryWritePortToHoldingUnlocked(start, value);
        }

        for (var i = 0; i < PortBitWidth; i++)
        {
            var on = TestPortBit(value, i);
            if (!TryWriteBitAtUnlocked(isOutput: false, start + i, on))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;
        if (!TryParseNative(address, out var area, out var addr))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                switch (area)
                {
                    case NativeArea.Coil:
                        if (!TryReadBitUnlocked(isOutput: true, addr, out var coil))
                        {
                            return false;
                        }

                        value = coil;
                        return true;
                    case NativeArea.Discrete:
                        if (!TryReadBitUnlocked(isOutput: false, addr, out var discrete))
                        {
                            return false;
                        }

                        value = discrete;
                        return true;
                    case NativeArea.Holding:
                        if (!TryReadRegisterUnlocked(holding: true, addr, out var holding))
                        {
                            return false;
                        }

                        value = holding;
                        return true;
                    case NativeArea.Input:
                        if (!TryReadRegisterUnlocked(holding: false, addr, out var input))
                        {
                            return false;
                        }

                        value = input;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _memory["driver.lastError"] = ex.Message;
                return false;
            }
        }
    }

    private bool TryWriteNativeAddress(string address, object? value)
    {
        if (!TryParseNative(address, out var area, out var addr))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                switch (area)
                {
                    case NativeArea.Coil:
                        var coil = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
                        return TryWriteBitAtUnlocked(isOutput: true, addr, coil);
                    case NativeArea.Holding:
                        if (!TryConvertToUInt16(value, out var reg))
                        {
                            return false;
                        }

                        return TryWriteRegisterUnlocked(addr, reg);
                    case NativeArea.Discrete:
                    case NativeArea.Input:
                        if (!_simulate)
                        {
                            return false;
                        }

                        if (area == NativeArea.Discrete)
                        {
                            var bit = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
                            return TryWriteBitAtUnlocked(isOutput: false, addr, bit);
                        }

                        if (!TryConvertToUInt16(value, out var inReg))
                        {
                            return false;
                        }

                        _simInputRegs[addr] = inReg;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _memory["driver.lastError"] = ex.Message;
                return false;
            }
        }
    }

    private bool TryReadPortWordUnlocked(bool isOutput, short type, out int value)
    {
        value = 0;
        var start = ResolveBitStart(isOutput, type);

        if ((isOutput && _doTarget == DoTarget.Holding)
            || (!isOutput && _diSource == DiSource.Holding))
        {
            return TryReadPortFromHoldingUnlocked(start, out value);
        }

        for (var i = 0; i < PortBitWidth; i++)
        {
            if (!TryReadBitUnlocked(isOutput, start + i, out var bit))
            {
                return false;
            }

            if (bit)
            {
                value |= 1 << i;
            }
        }

        return true;
    }

    private bool TryWritePortWordUnlocked(short doType, int value)
    {
        var start = ResolveBitStart(isOutput: true, doType);
        if (_doTarget == DoTarget.Holding)
        {
            return TryWritePortToHoldingUnlocked(start, value);
        }

        for (var i = 0; i < PortBitWidth; i++)
        {
            if (!TryWriteBitAtUnlocked(isOutput: true, start + i, TestPortBit(value, i)))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadPortFromHoldingUnlocked(int registerStart, out int value)
    {
        value = 0;
        if (!TryReadRegisterUnlocked(holding: true, registerStart, out var lo)
            || !TryReadRegisterUnlocked(holding: true, registerStart + 1, out var hi))
        {
            return false;
        }

        value = lo | (hi << 16);
        return true;
    }

    private bool TryWritePortToHoldingUnlocked(int registerStart, int value)
    {
        var lo = (ushort)(value & 0xFFFF);
        var hi = (ushort)((value >> 16) & 0xFFFF);
        return TryWriteRegisterUnlocked(registerStart, lo)
            && TryWriteRegisterUnlocked(registerStart + 1, hi);
    }

    private int ResolveBitStart(bool isOutput, short type)
    {
        var baseAddr = isOutput ? _doAddress : _diAddress;
        var useHolding = (isOutput && _doTarget == DoTarget.Holding)
            || (!isOutput && _diSource == DiSource.Holding);
        var stride = useHolding ? 2 : PortBitWidth;
        if (type is GtsIoType.Gpi or GtsIoType.Gpo)
        {
            return baseAddr;
        }

        return baseAddr + Math.Max(0, (int)type) * stride;
    }

    private bool TryReadBitUnlocked(bool isOutput, int address, out bool value)
    {
        value = false;
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }

        if (_simulate)
        {
            if (isOutput || _diSource == DiSource.Coils)
            {
                value = _simCoils.GetOrAdd(address, false);
            }
            else if (_diSource == DiSource.Holding)
            {
                return false;
            }
            else
            {
                value = _simDiscrete.GetOrAdd(address, false);
            }

            return true;
        }

        if (_master is null)
        {
            return false;
        }

        try
        {
            var bits = isOutput || _diSource == DiSource.Coils
                ? _master.ReadCoils(_unitId, (ushort)address, 1)
                : _master.ReadInputs(_unitId, (ushort)address, 1);
            if (bits is null || bits.Length == 0)
            {
                return false;
            }

            value = bits[0];
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryWriteBitAtUnlocked(bool isOutput, int address, bool value)
    {
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }

        if (!isOutput)
        {
            if (!_simulate)
            {
                return false;
            }

            if (_diSource == DiSource.Coils)
            {
                _simCoils[address] = value;
            }
            else
            {
                _simDiscrete[address] = value;
            }

            return true;
        }

        if (_simulate)
        {
            _simCoils[address] = value;
            return true;
        }

        if (_master is null)
        {
            return false;
        }

        try
        {
            _master.WriteSingleCoil(_unitId, (ushort)address, value);
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryReadRegisterUnlocked(bool holding, int address, out ushort value)
    {
        value = 0;
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }

        if (_simulate)
        {
            value = holding
                ? _simHolding.GetOrAdd(address, (ushort)0)
                : _simInputRegs.GetOrAdd(address, (ushort)0);
            return true;
        }

        if (_master is null)
        {
            return false;
        }

        try
        {
            var regs = holding
                ? _master.ReadHoldingRegisters(_unitId, (ushort)address, 1)
                : _master.ReadInputRegisters(_unitId, (ushort)address, 1);
            if (regs is null || regs.Length == 0)
            {
                return false;
            }

            value = regs[0];
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryWriteRegisterUnlocked(int address, ushort value)
    {
        if (address < 0 || address > ushort.MaxValue)
        {
            return false;
        }

        if (_simulate)
        {
            _simHolding[address] = value;
            return true;
        }

        if (_master is null)
        {
            return false;
        }

        try
        {
            _master.WriteSingleRegister(_unitId, (ushort)address, value);
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryAddressBitShift(short addressBit, out int shift)
    {
        shift = addressBit - _ioBitBase;
        return shift is >= 0 and <= 31;
    }

    private void CloseMasterUnlocked()
    {
        if (_master is IDisposable masterDisposable)
        {
            try { masterDisposable.Dispose(); } catch { /* ignore */ }
        }

        _master = null;

        if (_tcp is not null)
        {
            try { _tcp.Close(); } catch { /* ignore */ }
            try { _tcp.Dispose(); } catch { /* ignore */ }
            _tcp = null;
        }
    }

    private static bool TestPortBit(int word, int shift) => (word & (1 << shift)) != 0;

    private static int ApplyPortBit(int word, int shift, bool value)
    {
        var mask = 1 << shift;
        return value ? word | mask : word & ~mask;
    }

    private static bool TryParseNative(string address, out NativeArea area, out int addr)
    {
        area = default;
        addr = 0;
        var parts = address.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out addr)
            || addr is < 0 or > ushort.MaxValue)
        {
            return false;
        }

        area = parts[0].ToLowerInvariant() switch
        {
            "coil" or "coils" or "c" => NativeArea.Coil,
            "discrete" or "discret" or "di_bit" or "d" => NativeArea.Discrete,
            "holding" or "hold" or "hr" or "h" => NativeArea.Holding,
            "input" or "inputs" or "ir" or "i" => NativeArea.Input,
            _ => (NativeArea)(-1),
        };
        return (int)area >= 0;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case int i:
                result = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                result = (int)l;
                return true;
            case short s:
                result = s;
                return true;
            case ushort us:
                result = us;
                return true;
            case byte b:
                result = b;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result):
                return true;
            default:
                try
                {
                    result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }

    private static bool TryConvertToUInt16(object? value, out ushort result)
    {
        result = 0;
        if (!TryConvertToInt(value, out var n) || n is < 0 or > ushort.MaxValue)
        {
            return false;
        }

        result = (ushort)n;
        return true;
    }

    private static DiSource ParseDiSource(string raw)
    {
        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "coil" or "coils" or "0x" => DiSource.Coils,
            "holding" or "hold" or "4x" or "register" or "registers" => DiSource.Holding,
            _ => DiSource.Discrete,
        };
    }

    private static DoTarget ParseDoTarget(string raw)
    {
        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "holding" or "hold" or "4x" or "register" or "registers" => DoTarget.Holding,
            _ => DoTarget.Coils,
        };
    }

    private static short ParseIoBitBase(MdkSetting.DriverConfig config)
    {
        if (!config.Parameters.TryGetValue("ioBitBase", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var key = raw.Trim();
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n == 1 ? (short)1 : (short)0;
        }

        if (key.Equals("1base", StringComparison.OrdinalIgnoreCase)
            || key.Equals("one", StringComparison.OrdinalIgnoreCase)
            || key.Equals("true", StringComparison.OrdinalIgnoreCase)
            || key.Equals("gts", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static string GetString(MdkSetting.DriverConfig config, string key, string defaultValue)
        => config.Parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : defaultValue;

    private static int GetInt(MdkSetting.DriverConfig config, string key, int defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : defaultValue;
    }

    private static bool GetBool(MdkSetting.DriverConfig config, string key, bool defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        var keyText = raw.Trim();
        if (bool.TryParse(keyText, out var b))
        {
            return b;
        }

        if (keyText is "1" or "yes" or "on")
        {
            return true;
        }

        if (keyText is "0" or "no" or "off")
        {
            return false;
        }

        return defaultValue;
    }

    private enum DiSource
    {
        Discrete,
        Coils,
        Holding,
    }

    private enum DoTarget
    {
        Coils,
        Holding,
    }

    private enum NativeArea
    {
        Coil,
        Discrete,
        Holding,
        Input,
    }
}
