using System.Collections.Concurrent;
using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Drivers.Boards;

/// <summary>Live vendor backend for a catalog type. DLL is user-supplied; failures stay disconnected.</summary>
public sealed class NativeCardDriver : IDriver
{
    private readonly BoardKind _kind;
    private NativeMotionBackend? _backend;
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private int _disposed;

    public NativeCardDriver(BoardKind kind)
    {
        _kind = kind;
    }

    public string Name => _kind.Name;

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        CloseBackend();
        _memory.Clear();
        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.vendor"] = _kind.Vendor;
        _memory["driver.family"] = _kind.Family;
        var dll = config.Parameters is not null
                  && config.Parameters.TryGetValue("nativeDll", out var raw)
                  && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : _kind.NativeDll;
        NativeDllMap.Bind(_kind.NativeDll, dll);
        _memory["driver.nativeDll"] = dll;
        _memory["driver.mode"] = "live";
        _memory["driver.lastCode"] = -1;

        try
        {
            _backend = NativeMotionBackend.Create(_kind);
            if (_backend.TryOpen(config, _kind, out var error))
            {
                IsConnected = true;
                _memory["driver.lastCode"] = 0;
                return;
            }

            _memory["driver.lastError"] = string.IsNullOrWhiteSpace(error) ? "open_failed" : error;
            CloseBackend();
        }
        catch (DllNotFoundException ex)
        {
            _memory["driver.lastError"] = "native_dll_missing";
            _memory["driver.lastDetail"] = ex.Message;
            CloseBackend();
        }
        catch (BadImageFormatException ex)
        {
            _memory["driver.lastError"] = "native_bad_image";
            _memory["driver.lastDetail"] = ex.Message;
            CloseBackend();
        }
        catch (EntryPointNotFoundException ex)
        {
            _memory["driver.lastError"] = "native_entry_missing";
            _memory["driver.lastDetail"] = ex.Message;
            CloseBackend();
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            CloseBackend();
        }
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
            return TryReadIo(io, out value);
        }

        return _memory.TryGetValue(key, out value);
    }

    public bool Write(string address, object? value)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(address) || _backend is null)
        {
            return false;
        }

        if (DriverIoAddress.LooksLike(address) && DriverIoAddress.TryParse(address, out var io))
        {
            return TryWriteIo(io, value);
        }

        _memory[address.Trim()] = value;
        return true;
    }

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var word = 0;
        if (!Safe(() => _backend.TryReadDiPort(out word)))
        {
            return false;
        }

        value = word;
        return true;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var word = 0;
        if (!Safe(() => _backend.TryReadDoPort(out word)))
        {
            return false;
        }

        value = word;
        return true;
    }

    public bool WriteDo(short doType, int value)
        => IsConnected && Safe(() => _backend!.WriteDoPort(value));

    public bool WriteDoBit(short doType, short doIndex, bool value)
        => IsConnected && Safe(() => _backend!.WriteDoBit(doIndex, value));

    public bool EnableAxis(short axis) => IsConnected && Safe(() => _backend!.EnableAxis(axis, true));

    public bool DisableAxis(short axis) => IsConnected && Safe(() => _backend!.EnableAxis(axis, false));

    public bool IsAxisEnabled(short axis) => IsConnected && _backend!.IsAxisEnabled(axis);

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var raw = 0;
        if (!Safe(() => _backend.TryGetStatus(axis, out raw)))
        {
            return false;
        }

        status = raw;
        return true;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var pos = 0d;
        if (!Safe(() => _backend.TryGetPrfPos(axis, out pos)))
        {
            return false;
        }

        position = pos;
        return true;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var pos = 0d;
        if (!Safe(() => _backend.TryGetEncPos(axis, out pos)))
        {
            return false;
        }

        position = pos;
        return true;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected || _backend is null)
        {
            return false;
        }

        var vel = 0d;
        if (!Safe(() => _backend.TryGetVel(axis, out vel)))
        {
            return false;
        }

        velocity = vel;
        return true;
    }

    public bool SetAxisPosition(short axis, double position)
        => IsConnected && Safe(() => _backend!.SetPosition(axis, position));

    public bool SetAxisVelocity(short axis, double velocity)
        => IsConnected && Safe(() => _backend!.SetVelocity(axis, velocity));

    public bool SetAxisAcceleration(short axis, double acceleration)
        => IsConnected && Safe(() => _backend!.SetAcc(axis, acceleration));

    public bool SetAxisDeceleration(short axis, double deceleration)
        => IsConnected && Safe(() => _backend!.SetDec(axis, deceleration));

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => IsConnected && Safe(() => _backend!.MoveTrap(axis, targetPosition, velocity, acceleration, deceleration));

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
        => IsConnected && Safe(() => _backend!.MoveJog(axis, velocity, acceleration, deceleration));

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => IsConnected && Safe(() => _backend!.MoveHome(axis, homeMode, velocity, acceleration, deceleration));

    public bool Stop(int axisMask, int option = 0)
        => IsConnected && Safe(() => _backend!.Stop(axisMask, option));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CloseBackend();
    }

    private bool TryReadIo(DriverIoAddress io, out object? value)
    {
        value = null;
        if (_backend is null)
        {
            return false;
        }

        if (io.IsBit)
        {
            var bit = io.BitIndex!.Value;
            var on = false;
            var ok = io.IsOutput
                ? Safe(() => _backend.TryReadDoBit(bit, out on))
                : Safe(() => _backend.TryReadDiBit(bit, out on));
            return ok && Assign(out value, on);
        }

        var word = 0;
        var portOk = io.IsOutput
            ? Safe(() => _backend.TryReadDoPort(out word))
            : Safe(() => _backend.TryReadDiPort(out word));
        return portOk && Assign(out value, word);
    }

    private bool TryWriteIo(DriverIoAddress io, object? value)
    {
        if (_backend is null)
        {
            return false;
        }

        if (io.IsBit)
        {
            return Safe(() => _backend.WriteDoBit(io.BitIndex!.Value, ToBool(value)));
        }

        return io.IsOutput && Safe(() => _backend.WriteDoPort(ToInt(value)));
    }

    private bool Safe(Func<bool> call)
    {
        try
        {
            lock (_gate)
            {
                return call();
            }
        }
        catch (Exception ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return false;
        }
    }

    private void CloseBackend()
    {
        IsConnected = false;
        _backend?.Dispose();
        _backend = null;
    }

    private static bool Assign<T>(out T dest, T src)
    {
        dest = src;
        return true;
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
}
