using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Drivers.Boards;

/// <summary>
/// Motion + GPIO backend for catalog cards. Default <c>simulate=true</c> uses memory
/// (same <see cref="DriverIoAddress"/> as SIM/DMC). Live mode only connects when the
/// vendor DLL can be loaded — P/Invoke bindings are added per card later; SDK stays with the user.
/// </summary>
public sealed class SimulatedCardDriver : IDriver
{
    private readonly BoardKind _kind;
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<short, int> _ports = new();
    private readonly ConcurrentDictionary<short, AxisMem> _axes = new();
    private readonly object _gate = new();
    private short _ioBitBase;
    private int _disposed;

    public SimulatedCardDriver(BoardKind kind)
    {
        _kind = kind;
    }

    public string Name => _kind.Name;

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _ports.Clear();
        _axes.Clear();
        _memory.Clear();

        _ioBitBase = ParseIoBitBase(config, _kind.DefaultIoBitBase);
        var card = GetInt(config, "card", 0);
        var nativeDll = GetString(config, "nativeDll", _kind.NativeDll);
        var simulate = GetBool(config, "simulate", true);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.card"] = card;
        _memory["driver.vendor"] = _kind.Vendor;
        _memory["driver.family"] = _kind.Family;
        _memory["driver.nativeDll"] = nativeDll;
        _memory["driver.ioBitBase"] = _ioBitBase;
        _memory["driver.lastCode"] = 0;

        if (simulate)
        {
            _memory["driver.mode"] = "simulation";
            IsConnected = true;
            return;
        }

        if (TryLoadNative(nativeDll, out var reason))
        {
            _memory["driver.mode"] = "native-present";
            _memory["driver.lastError"] = "native_not_bound";
            IsConnected = false;
            return;
        }

        _memory["driver.mode"] = "disconnected";
        _memory["driver.lastError"] = reason;
        IsConnected = false;
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var key = address.Trim();
        if (!IsConnected)
        {
            return key.StartsWith("driver.", StringComparison.OrdinalIgnoreCase)
                && _memory.TryGetValue(key, out value);
        }

        if (DriverIoAddress.LooksLike(address) && DriverIoAddress.TryParse(address, out var io))
        {
            if (io.IsBit)
            {
                if (!TryReadPort(io.Type, out var word))
                {
                    return false;
                }

                value = TestBit(word, io.BitIndex!.Value);
                return true;
            }

            if (io.IsOutput)
            {
                return TryReadDo(io.Type, out var w) && Assign(out value, w);
            }

            return TryReadDi(io.Type, out var di) && Assign(out value, di);
        }

        return _memory.TryGetValue(address.Trim(), out value);
    }

    public bool Write(string address, object? value)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (DriverIoAddress.LooksLike(address) && DriverIoAddress.TryParse(address, out var io))
        {
            if (io.IsBit)
            {
                if (!io.IsOutput && io.Type != GtsIoType.Gpi && io.Type != GtsIoType.Home)
                {
                    return WriteBit(io.Type, io.BitIndex!.Value, ToBool(value));
                }

                return WriteBit(io.Type, io.BitIndex!.Value, ToBool(value));
            }

            if (!io.IsOutput)
            {
                _ports[io.Type] = ToInt(value);
                return true;
            }

            return WriteDo(io.Type, ToInt(value));
        }

        _memory[address.Trim()] = value;
        return true;
    }

    public bool TryReadDi(short diType, out int value) => TryReadPort(diType, out value);

    public bool TryReadDo(short doType, out int value) => TryReadPort(doType, out value);

    public bool WriteDo(short doType, int value)
    {
        if (!IsConnected)
        {
            return false;
        }

        _ports[doType] = value;
        return true;
    }

    public bool WriteDoBit(short doType, short doIndex, bool value) => WriteBit(doType, doIndex, value);

    public bool EnableAxis(short axis) => MutateAxis(axis, a => a.Enabled = true);

    public bool DisableAxis(short axis) => MutateAxis(axis, a =>
    {
        a.Enabled = false;
        a.Moving = false;
        a.Velocity = 0;
    });

    public bool IsAxisEnabled(short axis) => _axes.TryGetValue(axis, out var a) && a.Enabled;

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        if (!IsConnected || !_axes.TryGetValue(axis, out var a))
        {
            return false;
        }

        if (a.Enabled)
        {
            status |= 1 << 1;
        }

        if (a.Moving)
        {
            status |= 1 << 10;
        }

        return true;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!_axes.TryGetValue(axis, out var a))
        {
            return false;
        }

        position = a.Position;
        return true;
    }

    public bool TryGetAxisEncPosition(short axis, out double position) => TryGetAxisPrfPosition(axis, out position);

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!_axes.TryGetValue(axis, out var a))
        {
            return false;
        }

        velocity = a.Velocity;
        return true;
    }

    public bool SetAxisPosition(short axis, double position) => MutateAxis(axis, a => a.Position = position);

    public bool SetAxisVelocity(short axis, double velocity) => MutateAxis(axis, a => a.CommandVel = velocity);

    public bool SetAxisAcceleration(short axis, double acceleration) => MutateAxis(axis, a => a.Acc = acceleration);

    public bool SetAxisDeceleration(short axis, double deceleration) => MutateAxis(axis, a => a.Dec = deceleration);

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => MutateAxis(axis, a =>
        {
            a.Enabled = true;
            a.Position = targetPosition;
            a.Velocity = 0;
            a.CommandVel = velocity;
            a.Acc = acceleration;
            a.Dec = deceleration;
            a.Moving = false;
        });

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
        => MutateAxis(axis, a =>
        {
            a.Enabled = true;
            a.Velocity = velocity;
            a.CommandVel = velocity;
            a.Acc = acceleration;
            a.Dec = deceleration;
            a.Moving = true;
        });

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => MutateAxis(axis, a =>
        {
            a.Enabled = true;
            a.Position = 0;
            a.Velocity = 0;
            a.CommandVel = velocity;
            a.Acc = acceleration;
            a.Dec = deceleration;
            a.Moving = false;
            a.Homed = true;
            _memory[$"axis.{axis}.homeMode"] = homeMode;
        });

    public bool Stop(int axisMask, int option = 0)
    {
        if (!IsConnected)
        {
            return false;
        }

        lock (_gate)
        {
            foreach (var kv in _axes)
            {
                if (axisMask != 0 && ((axisMask >> kv.Key) & 1) == 0)
                {
                    continue;
                }

                kv.Value.Moving = false;
                kv.Value.Velocity = 0;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IsConnected = false;
        _ports.Clear();
        _axes.Clear();
    }

    private bool TryReadPort(short type, out int value) => _ports.TryGetValue(type, out value) || Assign(out value, 0);

    private bool WriteBit(short type, short nativeBit, bool on)
    {
        if (!IsConnected)
        {
            return false;
        }

        var mask = BitMask(nativeBit);
        _ports.AddOrUpdate(type, on ? mask : 0, (_, cur) => on ? cur | mask : cur & ~mask);
        return true;
    }

    private int BitMask(short nativeBit)
    {
        if (_ioBitBase == 1)
        {
            return nativeBit < 1 ? 0 : 1 << (nativeBit - 1);
        }

        return nativeBit < 0 ? 0 : 1 << nativeBit;
    }

    private bool TestBit(int word, short nativeBit) => (word & BitMask(nativeBit)) != 0;

    private bool MutateAxis(short axis, Action<AxisMem> mutate)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new AxisMem());
        lock (state.Gate)
        {
            mutate(state);
        }

        return true;
    }

    private static bool TryLoadNative(string dll, out string reason)
    {
        reason = "native_dll_missing";
        if (string.IsNullOrWhiteSpace(dll))
        {
            return false;
        }

        try
        {
            if (NativeLibrary.TryLoad(dll, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        return false;
    }

    private static short ParseIoBitBase(MdkSetting.DriverConfig config, short fallback)
    {
        if (config.Parameters is null
            || !config.Parameters.TryGetValue("ioBitBase", out var raw)
            || !short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return parsed is 0 or 1 ? parsed : fallback;
    }

    private static int GetInt(MdkSetting.DriverConfig config, string key, int fallback)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    private static string GetString(MdkSetting.DriverConfig config, string key, string fallback)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim();
    }

    private static bool GetBool(MdkSetting.DriverConfig config, string key, bool fallback)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return raw.Trim() switch
        {
            "1" or "true" or "True" or "yes" => true,
            "0" or "false" or "False" or "no" => false,
            _ => fallback,
        };
    }

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        int n => n != 0,
        string s => s is "1" or "true" or "True",
        _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
    };

    private static int ToInt(object? value) => value switch
    {
        int n => n,
        bool b => b ? 1 : 0,
        _ => Convert.ToInt32(value, CultureInfo.InvariantCulture),
    };

    private static bool Assign<T>(out T dest, T src)
    {
        dest = src;
        return true;
    }

    private sealed class AxisMem
    {
        public readonly object Gate = new();
        public bool Enabled;
        public bool Moving;
        public bool Homed;
        public double Position;
        public double Velocity;
        public double CommandVel;
        public double Acc = 10000;
        public double Dec = 10000;
    }
}
