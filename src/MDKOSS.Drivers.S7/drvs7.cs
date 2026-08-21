using System.Collections.Concurrent;
using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using S7.Net;

namespace MDKOSS.Drivers.S7;

/// <summary>
/// Siemens S7-1200 (and family) PLC driver over ISO-on-TCP via S7netplus.
/// Digital IO uses the same <see cref="DriverIoAddress"/> form as SIM/DMC
/// (<c>di.gpi.bit.n</c> / <c>do.gpo.bit.n</c>, 0-based by default).
/// When <c>simulate=true</c> or <c>host</c> is empty, IO is kept in memory (no PLC required).
/// Axis / interpolation APIs return false — this driver is an IO backend, not a motion card.
/// </summary>
public sealed class DrvS7 : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, byte> _simInput = new();
    private readonly ConcurrentDictionary<int, byte> _simOutput = new();
    private readonly DriverIoPortCache _ioCache = new();
    private readonly object _gate = new();
    private Plc? _plc;
    private bool _simulate;
    private short _ioBitBase;
    private int _diByteBase;
    private int _doByteBase;
    private int _disposed;

    public string Name => "S7";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _ioBitBase = ParseIoBitBase(config);
        _diByteBase = GetInt(config, "diByteBase", 0);
        _doByteBase = GetInt(config, "doByteBase", 0);
        var host = GetString(config, "host", string.Empty).Trim();
        var rack = (short)GetInt(config, "rack", 0);
        var slot = (short)GetInt(config, "slot", 1);
        var readTimeoutMs = GetInt(config, "readTimeoutMs", 3000);
        var writeTimeoutMs = GetInt(config, "writeTimeoutMs", 3000);
        var forceSimulate = GetBool(config, "simulate", host.Length == 0);
        var cpu = ParseCpu(GetString(config, "cpu", "S71200"));

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.host"] = host;
        _memory["driver.rack"] = rack;
        _memory["driver.slot"] = slot;
        _memory["driver.cpu"] = cpu.ToString();
        _memory["driver.ioBitBase"] = _ioBitBase;
        _memory["driver.diByteBase"] = _diByteBase;
        _memory["driver.doByteBase"] = _doByteBase;
        _memory["driver.lastCode"] = 0;

        ClosePlcUnlocked();
        _simInput.Clear();
        _simOutput.Clear();
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
            var plc = new Plc(cpu, host, rack, slot)
            {
                ReadTimeout = Math.Max(100, readTimeoutMs),
                WriteTimeout = Math.Max(100, writeTimeoutMs),
            };
            plc.Open();
            if (!plc.IsConnected)
            {
                _memory["driver.lastError"] = "open_failed";
                plc.Close();
                IsConnected = false;
                return;
            }

            _plc = plc;
            IsConnected = true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            ClosePlcUnlocked();
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
        // IDriver bit index is 0-based (debug grid). Address bit.{n} follows ioBitBase.
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
            ClosePlcUnlocked();
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

    /// <summary>Simulation / debug: seed an input port word (live PLC rejects DI writes).</summary>
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
        var byteOffset = ResolveByteBase(isOutput: false, diType);
        for (var i = 0; i < 4; i++)
        {
            var b = (byte)((value >> (8 * i)) & 0xFF);
            _simInput[byteOffset + i] = b;
        }

        return true;
    }

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;
        if (!LooksLikeS7Address(address))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                if (_simulate)
                {
                    return TryReadSimNativeUnlocked(address, out value);
                }

                if (_plc is null || !_plc.IsConnected)
                {
                    return false;
                }

                value = _plc.Read(address);
                _memory["driver.lastCode"] = 0;
                return value is not null;
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
        if (!LooksLikeS7Address(address))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                if (_simulate)
                {
                    return TryWriteSimNativeUnlocked(address, value);
                }

                if (_plc is null || !_plc.IsConnected)
                {
                    return false;
                }

                _plc.Write(address, value!);
                _memory["driver.lastCode"] = 0;
                return true;
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
        var byteOffset = ResolveByteBase(isOutput, type);
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            if (!TryReadByteUnlocked(isOutput, byteOffset + i, out bytes[i]))
            {
                return false;
            }
        }

        value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
        return true;
    }

    private bool TryWritePortWordUnlocked(short doType, int value)
    {
        var byteOffset = ResolveByteBase(isOutput: true, doType);
        for (var i = 0; i < 4; i++)
        {
            var b = (byte)((value >> (8 * i)) & 0xFF);
            if (!TryWriteByteUnlocked(isOutput: true, byteOffset + i, b))
            {
                return false;
            }
        }

        return true;
    }

    private int ResolveByteBase(bool isOutput, short type)
    {
        var baseOffset = isOutput ? _doByteBase : _diByteBase;
        if (type is GtsIoType.Gpi or GtsIoType.Gpo)
        {
            return baseOffset;
        }

        // Numeric / other type codes select additional 4-byte windows.
        return baseOffset + Math.Max(0, (int)type) * 4;
    }

    private bool TryReadByteUnlocked(bool isOutput, int byteAddress, out byte value)
    {
        value = 0;
        if (byteAddress < 0)
        {
            return false;
        }

        if (_simulate)
        {
            var map = isOutput ? _simOutput : _simInput;
            value = map.GetOrAdd(byteAddress, 0);
            return true;
        }

        if (_plc is null || !_plc.IsConnected)
        {
            return false;
        }

        try
        {
            var dataType = isOutput ? DataType.Output : DataType.Input;
            var bytes = _plc.ReadBytes(dataType, 0, byteAddress, 1);
            if (bytes is null || bytes.Length == 0)
            {
                return false;
            }

            value = bytes[0];
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryWriteByteUnlocked(bool isOutput, int byteAddress, byte value)
    {
        if (byteAddress < 0 || !isOutput)
        {
            return false;
        }

        if (_simulate)
        {
            _simOutput[byteAddress] = value;
            return true;
        }

        if (_plc is null || !_plc.IsConnected)
        {
            return false;
        }

        try
        {
            _plc.WriteBytes(DataType.Output, 0, byteAddress, new[] { value });
            return true;
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private bool TryReadSimNativeUnlocked(string address, out object? value)
    {
        value = null;
        if (!TryParseProcessBit(address, out var isOutput, out var byteAddr, out var bit)
            || bit is null)
        {
            if (TryParseProcessByte(address, out isOutput, out byteAddr))
            {
                return TryReadByteUnlocked(isOutput, byteAddr, out var b)
                    && (value = b) is not null;
            }

            return false;
        }

        if (!TryReadByteUnlocked(isOutput, byteAddr, out var current))
        {
            return false;
        }

        value = (current & (1 << bit.Value)) != 0;
        return true;
    }

    private bool TryWriteSimNativeUnlocked(string address, object? value)
    {
        if (TryParseProcessBit(address, out var isOutput, out var byteAddr, out var bit)
            && bit is not null)
        {
            if (!isOutput || !TryReadByteUnlocked(true, byteAddr, out var current))
            {
                return false;
            }

            var on = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            var next = on
                ? (byte)(current | (1 << bit.Value))
                : (byte)(current & ~(1 << bit.Value));
            return TryWriteByteUnlocked(true, byteAddr, next);
        }

        if (TryParseProcessByte(address, out isOutput, out byteAddr) && isOutput)
        {
            var b = Convert.ToByte(value ?? 0, CultureInfo.InvariantCulture);
            return TryWriteByteUnlocked(true, byteAddr, b);
        }

        return false;
    }

    private bool TryAddressBitShift(short addressBit, out int shift)
    {
        shift = addressBit - _ioBitBase;
        return shift is >= 0 and <= 31;
    }

    private void ClosePlcUnlocked()
    {
        if (_plc is null)
        {
            return;
        }

        try
        {
            if (_plc.IsConnected)
            {
                _plc.Close();
            }
        }
        catch
        {
            // ignore close errors
        }

        if (_plc is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _plc = null;
    }

    private static bool TestPortBit(int word, int shift) => (word & (1 << shift)) != 0;

    private static int ApplyPortBit(int word, int shift, bool value)
    {
        var mask = 1 << shift;
        return value ? word | mask : word & ~mask;
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

    private static bool LooksLikeS7Address(string address)
    {
        if (address.Length < 2)
        {
            return false;
        }

        var head = address[0];
        return head is 'I' or 'i' or 'Q' or 'q' or 'M' or 'm' or 'D' or 'd'
            || address.StartsWith("DB", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseProcessBit(string address, out bool isOutput, out int byteAddr, out int? bit)
    {
        isOutput = false;
        byteAddr = 0;
        bit = null;
        // I0.0 / Q12.3
        if (address.Length < 3)
        {
            return false;
        }

        var area = address[0];
        if (area is not ('I' or 'i' or 'Q' or 'q'))
        {
            return false;
        }

        isOutput = area is 'Q' or 'q';
        var parts = address[1..].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byteAddr)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitNo)
            || bitNo is < 0 or > 7)
        {
            return false;
        }

        bit = bitNo;
        return true;
    }

    private static bool TryParseProcessByte(string address, out bool isOutput, out int byteAddr)
    {
        isOutput = false;
        byteAddr = 0;
        // IB0 / QB10
        if (address.Length < 3)
        {
            return false;
        }

        if (address.StartsWith("IB", StringComparison.OrdinalIgnoreCase))
        {
            isOutput = false;
            return int.TryParse(address[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out byteAddr);
        }

        if (address.StartsWith("QB", StringComparison.OrdinalIgnoreCase))
        {
            isOutput = true;
            return int.TryParse(address[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out byteAddr);
        }

        return false;
    }

    private static CpuType ParseCpu(string raw)
    {
        var key = raw.Trim();
        if (Enum.TryParse<CpuType>(key, ignoreCase: true, out var cpu))
        {
            return cpu;
        }

        return key.ToUpperInvariant() switch
        {
            "S7-1200" or "1200" => CpuType.S71200,
            "S7-1500" or "1500" => CpuType.S71500,
            "S7-300" or "300" => CpuType.S7300,
            "S7-400" or "400" => CpuType.S7400,
            "S7-200" or "200" => CpuType.S7200,
            _ => CpuType.S71200,
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
}
