using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Drivers.Boards;

/// <summary>
/// GTS motion driver implementation backed by gts.dll.
/// </summary>
public sealed class DrvGts : IDriver
{
    private static readonly ConcurrentDictionary<short, object> CardLocks = new();

    private readonly ConcurrentDictionary<string, object?> _memory = new();
    private readonly ConcurrentDictionary<short, bool> _axisEnabled = new();
    private readonly DriverIoPortCache _ioCache = new();
    private readonly DriverAxisStateCache _axisCache = new();
    private short _cardNo = 1;
    private short _channel;
    private short _crd = 1;
    private short _interpCrd = 1;
    private bool _interpArmed;

    private object NativeLock => CardLocks.GetOrAdd(_cardNo, static _ => new object());

    public string Name => "GTS";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        _cardNo = GetShort(config, "cardNo", 1);
        _channel = GetShort(config, "channel", 0);
        _crd = GetShort(config, "crd", 1);
        if (_crd <= 0)
        {
            _crd = 1;
        }
        var openParam = GetShort(config, "openParam", 0);
        var resetOnInit = GetBool(config, "resetOnInit", false);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.cardNo"] = _cardNo;
        _memory["driver.channel"] = _channel;
        _memory["driver.crd"] = _crd;

        short rc;
        lock (NativeLock)
        {
            rc = NativeGts.GT_Open(_cardNo, _channel, openParam);
        }

        IsConnected = rc == 0;
        _memory["driver.lastCode"] = rc;

        if (!IsConnected)
        {
            return;
        }

        if (resetOnInit)
        {
            lock (NativeLock)
            {
                _ = NativeGts.GT_Reset(_cardNo);
            }
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

    // ──────────────────────────────────────────────
    //  IO Control
    // ──────────────────────────────────────────────

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

        lock (NativeLock)
        {
            if (_ioCache.TryGet(false, diType, out value))
            {
                return true;
            }

            var rc = NativeGts.GT_GetDi(_cardNo, diType, out var nativeValue);
            _memory["driver.lastCode"] = rc;
            value = nativeValue;
            if (rc != 0)
            {
                return false;
            }

            _ioCache.Set(false, diType, value);
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

        lock (NativeLock)
        {
            if (_ioCache.TryGet(true, doType, out value))
            {
                return true;
            }

            var rc = NativeGts.GT_GetDo(_cardNo, doType, out var nativeValue);
            _memory["driver.lastCode"] = rc;
            value = nativeValue;
            if (rc != 0)
            {
                return false;
            }

            _ioCache.Set(true, doType, value);
            return true;
        }
    }

    public bool WriteDo(short doType, int value)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_SetDo(_cardNo, doType, value));
        if (ok)
        {
            _ioCache.Invalidate(true, doType);
        }

        return ok;
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        if (!DriverIoAddress.IsGtsBit(doIndex))
        {
            return false;
        }

        var bit = value ? (short)1 : (short)0;
        var ok = IsConnected && Call(() => NativeGts.GT_SetDoBit(_cardNo, doType, doIndex, bit));
        if (ok)
        {
            _ioCache.Invalidate(true, doType);
        }

        return ok;
    }

    // ──────────────────────────────────────────────
    //  Axis Servo Control
    // ──────────────────────────────────────────────

    public bool EnableAxis(short axis)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_AxisOn(_cardNo, axis));
        if (ok)
        {
            _axisEnabled[axis] = true;
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    public bool DisableAxis(short axis)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_AxisOff(_cardNo, axis));
        if (ok)
        {
            _axisEnabled[axis] = false;
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    public bool IsAxisEnabled(short axis)
    {
        if (TryGetAxisStatus(axis, out var raw))
        {
            var on = AxisStatusBits.Test(raw, AxisStatusBits.ServoOn);
            _axisEnabled[axis] = on;
            return on;
        }

        return _axisEnabled.GetOrAdd(axis, false);
    }

    // ──────────────────────────────────────────────
    //  Axis Status
    // ──────────────────────────────────────────────

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        if (!IsConnected)
        {
            return false;
        }

        lock (NativeLock)
        {
            var rc = NativeGts.GT_GetSts(_cardNo, axis, out var nativeStatus, 1, out _);
            _memory["driver.lastCode"] = rc;
            status = nativeStatus;
            return rc == 0;
        }
    }

    public bool TryGetAxisState(short axis, out AxisStatus status)
    {
        status = default;
        if (!IsConnected)
        {
            return false;
        }

        if (_axisCache.TryGet(axis, out status))
        {
            return true;
        }

        lock (NativeLock)
        {
            if (_axisCache.TryGet(axis, out status))
            {
                return true;
            }

            if (!TryGetAxisStatus(axis, out var raw))
            {
                return false;
            }

            var home = false;
            if (TryReadDi(GtsIoType.Home, out var homeWord))
            {
                home = DriverIoAddress.TestBit(homeWord, axis);
            }

            TryGetAxisPrfPosition(axis, out var prf);
            TryGetAxisEncPosition(axis, out var enc);
            TryGetAxisVelocity(axis, out var vel);

            status = AxisStatus.FromGts(raw, home, prf, enc, vel);
            _axisEnabled[axis] = status.ServoOn;
            _axisCache.Set(axis, status);
            return true;
        }
    }

    public bool TryGetAxisStates(short[] axes, AxisStatus[] statuses)
    {
        if (axes is null || statuses is null || axes.Length == 0 || statuses.Length < axes.Length)
        {
            return false;
        }

        if (!IsConnected)
        {
            return false;
        }

        lock (NativeLock)
        {
            var allCached = true;
            for (var i = 0; i < axes.Length; i++)
            {
                if (!_axisCache.TryGet(axes[i], out statuses[i]))
                {
                    allCached = false;
                    break;
                }
            }

            if (allCached)
            {
                return true;
            }

            short min = axes[0];
            short max = axes[0];
            for (var i = 1; i < axes.Length; i++)
            {
                if (axes[i] < min)
                {
                    min = axes[i];
                }

                if (axes[i] > max)
                {
                    max = axes[i];
                }
            }

            var span = max - min + 1;
            if (min < 1 || span is < 1 or > 16)
            {
                for (var i = 0; i < axes.Length; i++)
                {
                    if (!TryGetAxisState(axes[i], out statuses[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            var sts = new int[span];
            var prf = new double[span];
            var enc = new double[span];
            var rc = NativeGts.GT_GetStsN(_cardNo, min, sts, (short)span, out _);
            _memory["driver.lastCode"] = rc;
            if (rc != 0
                || NativeGts.GT_GetPrfPosN(_cardNo, min, prf, (short)span, out _) != 0
                || NativeGts.GT_GetEncPosN(_cardNo, min, enc, (short)span, out _) != 0)
            {
                return false;
            }

            var homeWord = 0;
            _ = TryReadDi(GtsIoType.Home, out homeWord);

            for (var i = 0; i < axes.Length; i++)
            {
                var axis = axes[i];
                var idx = axis - min;
                if (idx < 0 || idx >= span)
                {
                    return false;
                }

                TryGetAxisVelocity(axis, out var vel);
                var home = DriverIoAddress.TestBit(homeWord, axis);
                var state = AxisStatus.FromGts(sts[idx], home, prf[idx], enc[idx], vel);
                statuses[i] = state;
                _axisEnabled[axis] = state.ServoOn;
                _axisCache.Set(axis, state);
            }

            return true;
        }
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        lock (NativeLock)
        {
            var rc = NativeGts.GT_GetPrfPos(_cardNo, axis, out var nativePos, 1, out _);
            _memory["driver.lastCode"] = rc;
            position = nativePos;
            return rc == 0;
        }
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        lock (NativeLock)
        {
            var rc = NativeGts.GT_GetEncPos(_cardNo, axis, out var nativePos, 1, out _);
            _memory["driver.lastCode"] = rc;
            position = nativePos;
            return rc == 0;
        }
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected)
        {
            return false;
        }

        lock (NativeLock)
        {
            var rc = NativeGts.GT_GetVel(_cardNo, axis, out var nativeVel);
            _memory["driver.lastCode"] = rc;
            velocity = nativeVel;
            return rc == 0;
        }
    }

    // ──────────────────────────────────────────────
    //  Position Setting
    // ──────────────────────────────────────────────

    public bool SetAxisPosition(short axis, double position)
    {
        if (!IsConnected)
        {
            return false;
        }

        return CallAxis(axis, () => NativeGts.GT_SetPrfPos(_cardNo, axis, (int)position));
    }

    // ──────────────────────────────────────────────
    //  Motion Parameters
    // ──────────────────────────────────────────────

    public bool SetAxisVelocity(short axis, double velocity)
    {
        return IsConnected && CallAxis(axis, () => NativeGts.GT_SetVel(_cardNo, axis, velocity));
    }

    public bool SetAxisAcceleration(short axis, double acceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm { acc = acceleration, dec = 0, velStart = 0, smoothTime = 0 };
        return CallAxis(axis, () => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap));
    }

    public bool SetAxisDeceleration(short axis, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm { acc = 0, dec = deceleration, velStart = 0, smoothTime = 0 };
        return CallAxis(axis, () => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap));
    }

    // ──────────────────────────────────────────────
    //  Motion Control
    // ──────────────────────────────────────────────

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm
        {
            acc = acceleration,
            dec = deceleration,
            velStart = 0,
            smoothTime = 0
        };

        if (!Call(() => NativeGts.GT_PrfTrap(_cardNo, axis))
            || !Call(() => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap))
            || !Call(() => NativeGts.GT_SetPos(_cardNo, axis, targetPosition))
            || !Call(() => NativeGts.GT_SetVel(_cardNo, axis, velocity)))
        {
            return false;
        }

        var mask = 1 << (axis - 1);
        var ok = Call(() => NativeGts.GT_Update(_cardNo, mask));
        if (ok)
        {
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var jog = new NativeGts.TJogPrm
        {
            acc = acceleration,
            dec = deceleration,
            smooth = 0
        };

        if (!Call(() => NativeGts.GT_PrfJog(_cardNo, axis))
            || !Call(() => NativeGts.GT_SetJogPrm(_cardNo, axis, ref jog))
            || !Call(() => NativeGts.GT_SetVel(_cardNo, axis, velocity)))
        {
            return false;
        }

        var mask = 1 << (axis - 1);
        var ok = Call(() => NativeGts.GT_Update(_cardNo, mask));
        if (ok)
        {
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var home = new NativeGts.THomePrm
        {
            mode = homeMode,
            vel = velocity,
            acc = acceleration,
            dec = deceleration,
            velStart = 0,
            smoothTime = 0,
            homeOffset = 0,
            escapeStep = 0
        };

        if (!Call(() => NativeGts.GT_PrfHome(_cardNo, axis))
            || !Call(() => NativeGts.GT_SetHomePrm(_cardNo, axis, ref home)))
        {
            return false;
        }

        var mask = 1 << (axis - 1);
        var ok = Call(() => NativeGts.GT_Update(_cardNo, mask));
        if (ok)
        {
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    public bool Stop(int axisMask, int option = 0)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_Stop(_cardNo, axisMask, option));
        if (ok)
        {
            _axisCache.InvalidateAll();
        }

        return ok;
    }

    public bool MoveLine(short[] axes, double[] targets, double velocity, double acceleration, double deceleration, short crd = 0)
    {
        string? error = null;
        if (!IsConnected || !DriverInterp.TryValidateLine(axes, targets, velocity, acceleration, deceleration, out error))
        {
            if (error != null)
            {
                _memory["interp.error"] = error;
            }

            return false;
        }

        if (axes.Length is < 2 or > 3)
        {
            _memory["interp.error"] = "GTS line interpolation supports 2 or 3 axes.";
            return false;
        }

        var coord = ResolveCrd(crd);
        var vel = Math.Abs(velocity);
        var acc = Math.Abs(acceleration);
        if (!EnsureCrd(coord, axes, vel, acc)
            || !Call(() => NativeGts.GT_CrdClear(_cardNo, coord, 0)))
        {
            return false;
        }

        var ok = axes.Length == 2
            ? Call(() => NativeGts.GT_LnXY(
                _cardNo, coord, ToPulse(targets[0]), ToPulse(targets[1]), vel, acc, 0, 0))
            : Call(() => NativeGts.GT_LnXYZ(
                _cardNo, coord, ToPulse(targets[0]), ToPulse(targets[1]), ToPulse(targets[2]), vel, acc, 0, 0));
        if (!ok || !Call(() => NativeGts.GT_CrdStart(_cardNo, coord, 0)))
        {
            return false;
        }

        _interpCrd = coord;
        _interpArmed = true;
        _axisCache.InvalidateAll();
        return true;
    }

    public bool MoveArc(
        short[] axes,
        double[] targets,
        double[] center,
        bool clockwise,
        double velocity,
        double acceleration,
        double deceleration,
        short crd = 0)
    {
        string? error = null;
        if (!IsConnected
            || !DriverInterp.TryValidateArc(axes, targets, center, velocity, acceleration, deceleration, out error))
        {
            if (error != null)
            {
                _memory["interp.error"] = error;
            }

            return false;
        }

        if (axes.Length != 2)
        {
            _memory["interp.error"] = "GTS arc interpolation supports exactly 2 axes.";
            return false;
        }

        if (!TryGetAxisPrfPosition(axes[0], out var x0) || !TryGetAxisPrfPosition(axes[1], out var y0))
        {
            return false;
        }

        var coord = ResolveCrd(crd);
        var vel = Math.Abs(velocity);
        var acc = Math.Abs(acceleration);
        if (!EnsureCrd(coord, axes, vel, acc)
            || !Call(() => NativeGts.GT_CrdClear(_cardNo, coord, 0)))
        {
            return false;
        }

        // GT_ArcXYC center is an offset from the start point.
        var ok = Call(() => NativeGts.GT_ArcXYC(
            _cardNo,
            coord,
            ToPulse(targets[0]),
            ToPulse(targets[1]),
            center[0] - x0,
            center[1] - y0,
            (short)(clockwise ? 0 : 1),
            vel,
            acc,
            0,
            0));
        if (!ok || !Call(() => NativeGts.GT_CrdStart(_cardNo, coord, 0)))
        {
            return false;
        }

        _interpCrd = coord;
        _interpArmed = true;
        _axisCache.InvalidateAll();
        return true;
    }

    public bool TryGetInterpState(out bool moving, out double progress)
    {
        moving = false;
        progress = 0;
        if (!IsConnected)
        {
            return false;
        }

        if (!_interpArmed)
        {
            return true;
        }

        short run = 0;
        if (!Call(() => NativeGts.GT_CrdStatus(_cardNo, _interpCrd, out run, out _, 0)))
        {
            return false;
        }

        moving = run != 0;
        progress = moving ? 0 : 1;
        if (!moving)
        {
            _interpArmed = false;
        }

        return true;
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (IsConnected)
        {
            lock (NativeLock)
            {
                _ = NativeGts.GT_Close(_cardNo);
            }
        }

        IsConnected = false;
        _memory.Clear();
        _axisEnabled.Clear();
        _ioCache.Clear();
        _axisCache.InvalidateAll();
    }

    // ──────────────────────────────────────────────
    //  Address-based read/write helpers
    // ──────────────────────────────────────────────

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

            if (io.IsBit && !DriverIoAddress.IsGtsBit(io.BitIndex!.Value))
            {
                return false;
            }

            value = io.IsBit ? DriverIoAddress.TestBit(doValue, io.BitIndex!.Value) : doValue;
            return true;
        }

        if (!TryReadDi(io.Type, out var diValue))
        {
            return false;
        }

        if (io.IsBit && !DriverIoAddress.IsGtsBit(io.BitIndex!.Value))
        {
            return false;
        }

        value = io.IsBit ? DriverIoAddress.TestBit(diValue, io.BitIndex!.Value) : diValue;
        return true;
    }

    private bool TryWriteIoAddress(string address, object? value)
    {
        if (!DriverIoAddress.TryParse(address, out var io) || !io.IsOutput)
        {
            return false;
        }

        if (io.IsBit)
        {
            return WriteDoBit(io.Type, io.BitIndex!.Value, Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture));
        }

        // Whole-port write is a bitmask; bool would wipe every other bit via GT_SetDo.
        if (value is bool || !TryConvertToInt(value, out var doValue))
        {
            return false;
        }

        return WriteDo(io.Type, doValue);
    }

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;

        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            // Support extended address patterns: axis.{N}.enc, axis.{N}.status, axis.{N}.vel
            var suffix = ExtractSuffixAfterIndex(address, "axis.");
            if (suffix is not null)
            {
                if (suffix.Equals("enc", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisEncPosition(axis, out var encPos))
                    {
                        value = encPos;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisStatus(axis, out var status))
                    {
                        value = status;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("vel", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisVelocity(axis, out var vel))
                    {
                        value = vel;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                {
                    value = IsAxisEnabled(axis);
                    return true;
                }
            }

            // Default: return profile position
            if (TryGetAxisPrfPosition(axis, out var pos))
            {
                value = pos;
                return true;
            }
            return false;
        }

        return false;
    }

    private bool TryWriteNativeAddress(string address, object? value)
    {
        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            if (!TryConvertToInt(value, out var target))
            {
                return false;
            }

            const double defaultVel = 1000;
            const double defaultAcc = 10000;
            const double defaultDec = 10000;
            return MoveAxisTrap(axis, target, defaultVel, defaultAcc, defaultDec);
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    private bool Call(Func<short> invoke)
    {
        lock (NativeLock)
        {
            var rc = invoke();
            _memory["driver.lastCode"] = rc;
            return rc == 0;
        }
    }

    private bool CallAxis(short axis, Func<short> invoke)
    {
        var ok = Call(invoke);
        if (ok)
        {
            _axisCache.Invalidate(axis);
        }

        return ok;
    }

    private short ResolveCrd(short crd) => crd <= 0 ? _crd : crd;

    private static int ToPulse(double value) => (int)Math.Round(value);

    private bool EnsureCrd(short crd, short[] axes, double velocity, double acceleration)
    {
        var prm = new NativeGts.TCrdPrm
        {
            dimension = (short)axes.Length,
            synVelMax = (short)Math.Clamp(Math.Abs(velocity), 1, short.MaxValue),
            synAccMax = (short)Math.Clamp(Math.Abs(acceleration), 1, short.MaxValue),
            evenTime = 0,
            profile1 = axes[0],
            profile2 = axes[1],
            profile3 = axes.Length > 2 ? axes[2] : (short)0,
            setOriginFlag = 0,
        };
        return Call(() => NativeGts.GT_SetCrdPrm(_cardNo, crd, ref prm));
    }

    /// <summary>
    /// Extracts the sub-property suffix after the numeric index in an address like "axis.1.enc".
    /// Returns null if no suffix exists (plain "axis.{N}" pattern).
    /// </summary>
    private static string? ExtractSuffixAfterIndex(string address, string prefix)
    {
        var rest = address[prefix.Length..];
        var dotIdx = rest.IndexOf('.');
        if (dotIdx < 0)
        {
            return null;
        }

        // Ensure the part before the dot is a valid index
        var indexPart = rest[..dotIdx];
        if (!short.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return rest[(dotIdx + 1)..];
    }

    private static short GetShort(MdkSetting.DriverConfig config, string key, short defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool GetBool(MdkSetting.DriverConfig config, string key, bool defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            result = b ? 1 : 0;
            return true;
        }

        if (value is IConvertible c)
        {
            try
            {
                result = c.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryParseTypeAndIndex(string address, string prefix, out short value)
    {
        value = 0;
        if (!address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = address[prefix.Length..];
        // Only parse the numeric part (before any extra dot suffix like "axis.1.enc")
        var dotIdx = suffix.IndexOf('.');
        var numPart = dotIdx >= 0 ? suffix[..dotIdx] : suffix;
        return short.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static class NativeGts
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct TTrapPrm
        {
            public double acc;
            public double dec;
            public double velStart;
            public short smoothTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TJogPrm
        {
            public double acc;
            public double dec;
            public double smooth;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct THomePrm
        {
            public short mode;
            public short homeOffset;
            public short escapeStep;
            public double vel;
            public double acc;
            public double dec;
            public double velStart;
            public short smoothTime;
        }

        // ── Connection ──
        [DllImport("gts.dll")]
        internal static extern short GT_Open(short cardNum, short channel, short param);

        [DllImport("gts.dll")]
        internal static extern short GT_Close(short cardNum);

        [DllImport("gts.dll")]
        internal static extern short GT_Reset(short cardNum);

        // ── IO ──
        [DllImport("gts.dll")]
        internal static extern short GT_GetDi(short cardNum, short diType, out int pValue);

        [DllImport("gts.dll")]
        internal static extern short GT_GetDo(short cardNum, short doType, out int pValue);

        [DllImport("gts.dll")]
        internal static extern short GT_SetDo(short cardNum, short doType, int value);

        [DllImport("gts.dll")]
        internal static extern short GT_SetDoBit(short cardNum, short doType, short doIndex, short value);

        // ── Servo ──
        [DllImport("gts.dll")]
        internal static extern short GT_AxisOn(short cardNum, short axis);

        [DllImport("gts.dll")]
        internal static extern short GT_AxisOff(short cardNum, short axis);

        // ── Axis Status ──
        [DllImport("gts.dll")]
        internal static extern short GT_GetSts(short cardNum, short axis, out int pStatus, short count, out uint pClock);

        [DllImport("gts.dll", EntryPoint = "GT_GetSts")]
        internal static extern short GT_GetStsN(short cardNum, short axis, [Out] int[] pStatus, short count, out uint pClock);

        [DllImport("gts.dll")]
        internal static extern short GT_GetPrfPos(short cardNum, short profile, out double pValue, short count, out uint pClock);

        [DllImport("gts.dll", EntryPoint = "GT_GetPrfPos")]
        internal static extern short GT_GetPrfPosN(short cardNum, short profile, [Out] double[] pValue, short count, out uint pClock);

        [DllImport("gts.dll")]
        internal static extern short GT_GetEncPos(short cardNum, short profile, out double pValue, short count, out uint pClock);

        [DllImport("gts.dll", EntryPoint = "GT_GetEncPos")]
        internal static extern short GT_GetEncPosN(short cardNum, short profile, [Out] double[] pValue, short count, out uint pClock);

        [DllImport("gts.dll")]
        internal static extern short GT_GetVel(short cardNum, short profile, out double pValue);

        // ── Position Setting ──
        [DllImport("gts.dll")]
        internal static extern short GT_SetPrfPos(short cardNum, short profile, int pos);

        // ── Motion Parameters ──
        [DllImport("gts.dll")]
        internal static extern short GT_SetVel(short cardNum, short profile, double vel);

        [DllImport("gts.dll")]
        internal static extern short GT_SetTrapPrm(short cardNum, short profile, ref TTrapPrm pPrm);

        [DllImport("gts.dll")]
        internal static extern short GT_SetJogPrm(short cardNum, short profile, ref TJogPrm pPrm);

        [DllImport("gts.dll")]
        internal static extern short GT_SetHomePrm(short cardNum, short profile, ref THomePrm pPrm);

        // ── Motion Mode ──
        [DllImport("gts.dll")]
        internal static extern short GT_PrfTrap(short cardNum, short profile);

        [DllImport("gts.dll")]
        internal static extern short GT_PrfJog(short cardNum, short profile);

        [DllImport("gts.dll")]
        internal static extern short GT_PrfHome(short cardNum, short profile);

        [DllImport("gts.dll")]
        internal static extern short GT_SetPos(short cardNum, short profile, int pos);

        // ── Motion Execution ──
        [DllImport("gts.dll")]
        internal static extern short GT_Update(short cardNum, int mask);

        [DllImport("gts.dll")]
        internal static extern short GT_Stop(short cardNum, int mask, int option);

        [StructLayout(LayoutKind.Sequential)]
        internal struct TCrdPrm
        {
            public short dimension;
            public short synVelMax;
            public short synAccMax;
            public short evenTime;
            public short profile1;
            public short profile2;
            public short profile3;
            public short profile4;
            public short profile5;
            public short profile6;
            public short profile7;
            public short profile8;
            public int originPos1;
            public int originPos2;
            public int originPos3;
            public short setOriginFlag;
        }

        [DllImport("gts.dll")]
        internal static extern short GT_SetCrdPrm(short cardNum, short crd, ref TCrdPrm pCrdPrm);

        [DllImport("gts.dll")]
        internal static extern short GT_CrdClear(short cardNum, short crd, short fifo);

        [DllImport("gts.dll")]
        internal static extern short GT_LnXY(
            short cardNum, short crd, int x, int y, double synVel, double synAcc, double velEnd, short fifo);

        [DllImport("gts.dll")]
        internal static extern short GT_LnXYZ(
            short cardNum, short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, short fifo);

        [DllImport("gts.dll")]
        internal static extern short GT_ArcXYC(
            short cardNum,
            short crd,
            int x,
            int y,
            double xCenter,
            double yCenter,
            short circleDir,
            double synVel,
            double synAcc,
            double velEnd,
            short fifo);

        [DllImport("gts.dll")]
        internal static extern short GT_CrdStart(short cardNum, short crd, short fifo);

        [DllImport("gts.dll")]
        internal static extern short GT_CrdStatus(short cardNum, short crd, out short pRun, out int pSegment, short fifo);
    }
}
