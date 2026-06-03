using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// GTS motion driver implementation backed by gts.dll.
/// </summary>
public sealed class DrvGts : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new();
    private readonly ConcurrentDictionary<short, bool> _axisEnabled = new();
    private short _cardNo = 1;
    private short _channel;

    public string Name => "GTS";

    public bool IsConnected { get; private set; }

    public void Initialize(MDKOSS.Core.MdkSetting.DriverConfig config)
    {
        _cardNo = GetShort(config, "cardNo", 1);
        _channel = GetShort(config, "channel", 0);
        var openParam = GetShort(config, "openParam", 0);
        var resetOnInit = GetBool(config, "resetOnInit", false);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.cardNo"] = _cardNo;
        _memory["driver.channel"] = _channel;

        var rc = NativeGts.GT_Open(_cardNo, _channel, openParam);
        IsConnected = rc == 0;
        _memory["driver.lastCode"] = rc;

        if (!IsConnected)
        {
            return;
        }

        if (resetOnInit)
        {
            _ = NativeGts.GT_Reset(_cardNo);
        }
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (TryReadNativeAddress(address, out value))
        {
            return true;
        }

        return _memory.TryGetValue(address, out value);
    }

    public bool Write(string address, object? value)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var handledByNative = TryWriteNativeAddress(address, value);
        if (handledByNative)
        {
            _memory[address] = value;
            return true;
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

        var rc = NativeGts.GT_GetDi(_cardNo, diType, out var nativeValue);
        _memory["driver.lastCode"] = rc;
        value = nativeValue;
        return rc == 0;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetDo(_cardNo, doType, out var nativeValue);
        _memory["driver.lastCode"] = rc;
        value = nativeValue;
        return rc == 0;
    }

    public bool WriteDo(short doType, int value)
    {
        return IsConnected && Call(() => NativeGts.GT_SetDo(_cardNo, doType, value));
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        var bit = value ? (short)1 : (short)0;
        return IsConnected && Call(() => NativeGts.GT_SetDoBit(_cardNo, doType, doIndex, bit));
    }

    // ──────────────────────────────────────────────
    //  Axis Servo Control
    // ──────────────────────────────────────────────

    public bool EnableAxis(short axis)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_AxisOn(_cardNo, axis));
        if (ok) _axisEnabled[axis] = true;
        return ok;
    }

    public bool DisableAxis(short axis)
    {
        var ok = IsConnected && Call(() => NativeGts.GT_AxisOff(_cardNo, axis));
        if (ok) _axisEnabled[axis] = false;
        return ok;
    }

    public bool IsAxisEnabled(short axis)
    {
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

        var rc = NativeGts.GT_GetSts(_cardNo, axis, out var nativeStatus, 1, out _);
        _memory["driver.lastCode"] = rc;
        status = nativeStatus;
        return rc == 0;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetPrfPos(_cardNo, axis, out var nativePos, 1, out _);
        _memory["driver.lastCode"] = rc;
        position = nativePos;
        return rc == 0;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetEncPos(_cardNo, axis, out var nativePos, 1, out _);
        _memory["driver.lastCode"] = rc;
        position = nativePos;
        return rc == 0;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetVel(_cardNo, axis, out var nativeVel);
        _memory["driver.lastCode"] = rc;
        velocity = nativeVel;
        return rc == 0;
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

        return Call(() => NativeGts.GT_SetPrfPos(_cardNo, axis, (int)position));
    }

    // ──────────────────────────────────────────────
    //  Motion Parameters
    // ──────────────────────────────────────────────

    public bool SetAxisVelocity(short axis, double velocity)
    {
        return IsConnected && Call(() => NativeGts.GT_SetVel(_cardNo, axis, velocity));
    }

    public bool SetAxisAcceleration(short axis, double acceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm { acc = acceleration, dec = 0, velStart = 0, smoothTime = 0 };
        return Call(() => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap));
    }

    public bool SetAxisDeceleration(short axis, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm { acc = 0, dec = deceleration, velStart = 0, smoothTime = 0 };
        return Call(() => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap));
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
        return Call(() => NativeGts.GT_Update(_cardNo, mask));
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
        return Call(() => NativeGts.GT_Update(_cardNo, mask));
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
        return Call(() => NativeGts.GT_Update(_cardNo, mask));
    }

    public bool Stop(int axisMask, int option = 0)
    {
        return IsConnected && Call(() => NativeGts.GT_Stop(_cardNo, axisMask, option));
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (IsConnected)
        {
            _ = NativeGts.GT_Close(_cardNo);
        }

        IsConnected = false;
        _memory.Clear();
        _axisEnabled.Clear();
    }

    // ──────────────────────────────────────────────
    //  Address-based read/write helpers
    // ──────────────────────────────────────────────

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;

        if (TryParseTypeAndIndex(address, "di.", out var diType))
        {
            if (TryReadDi(diType, out var diValue))
            {
                value = diValue;
                return true;
            }
            return false;
        }

        if (TryParseTypeAndIndex(address, "do.", out var doType))
        {
            if (TryReadDo(doType, out var doValue))
            {
                value = doValue;
                return true;
            }
            return false;
        }

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
        if (TryParseTypeAndIndex(address, "do.", out var doType))
        {
            if (!TryConvertToInt(value, out var doValue))
            {
                return false;
            }
            return WriteDo(doType, doValue);
        }

        if (TryParseDoBitAddress(address, out var doBitType, out var doBitIndex))
        {
            return WriteDoBit(doBitType, doBitIndex, Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture));
        }

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
        var rc = invoke();
        _memory["driver.lastCode"] = rc;
        return rc == 0;
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

    private static short GetShort(MDKOSS.Core.MdkSetting.DriverConfig config, string key, short defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool GetBool(MDKOSS.Core.MdkSetting.DriverConfig config, string key, bool defaultValue)
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

    private static bool TryParseDoBitAddress(string address, out short doType, out short doIndex)
    {
        doType = 0;
        doIndex = 0;
        if (!address.StartsWith("do.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = address.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts[2].Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return short.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out doType)
            && short.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out doIndex);
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

        [DllImport("gts.dll")]
        internal static extern short GT_GetPrfPos(short cardNum, short profile, out double pValue, short count, out uint pClock);

        [DllImport("gts.dll")]
        internal static extern short GT_GetEncPos(short cardNum, short profile, out double pValue, short count, out uint pClock);

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
    }
}
