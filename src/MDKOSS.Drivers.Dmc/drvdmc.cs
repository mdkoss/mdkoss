using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using LTDMC = csLTDMC.LTDMC;

namespace MDKOSS.Drivers.Dmc;

/// <summary>
/// Leadshine LTDMC driver. GPIO addresses use the same <see cref="DriverIoAddress"/> form as GTS/SIM
/// (<c>di.gpi.bit.n</c> / <c>do.gpo.bit.n</c>, DMC-native 0-based). Mapped 1:1 to
/// <c>dmc_read_inbit</c> / <c>dmc_write_outbit</c>.
/// </summary>
public sealed class DrvDmc : IDriver
{
    private static readonly object BoardLock = new();
    private static int BoardRefCount;

    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<short, bool> _axisEnabled = new();
    private readonly ConcurrentDictionary<short, AxisMotionPrm> _motion = new();
    private ushort _cardNo;
    private bool _sevonActiveLow = true;
    private bool _ownsBoard;

    public string Name => "DMC";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        _cardNo = GetUShort(config, "card", 0);
        _sevonActiveLow = GetBool(config, "sevonActiveLow", true);
        var resetOnInit = GetBool(config, "resetOnInit", false);
        config.Parameters.TryGetValue("configPath", out var configPath);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.card"] = _cardNo;

        try
        {
            lock (BoardLock)
            {
                if (BoardRefCount == 0)
                {
                    var cards = LTDMC.dmc_board_init();
                    _memory["driver.lastCode"] = cards;
                    if (cards <= 0)
                    {
                        return;
                    }
                }

                BoardRefCount++;
                _ownsBoard = true;
            }
        }
        catch (DllNotFoundException ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return;
        }
        catch (BadImageFormatException ex)
        {
            _memory["driver.lastError"] = ex.Message;
            return;
        }

        IsConnected = true;

        if (!string.IsNullOrWhiteSpace(configPath)
            && !Call(() => LTDMC.dmc_download_configfile(_cardNo, configPath.Trim())))
        {
            IsConnected = false;
            ReleaseBoard();
            return;
        }

        if (resetOnInit)
        {
            _ = LTDMC.dmc_soft_reset(_cardNo);
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

        if (TryReadAxisAddress(address, out value))
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

        if (TryWriteAxisAddress(address, value))
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
    //  IO Control (IDriver group = DMC port 0,1,2…)
    // ──────────────────────────────────────────────

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        if (!IsConnected || diType < 0)
        {
            return false;
        }

        value = unchecked((int)LTDMC.dmc_read_inport(_cardNo, (ushort)diType));
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected || doType < 0)
        {
            return false;
        }

        value = unchecked((int)LTDMC.dmc_read_outport(_cardNo, (ushort)doType));
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool WriteDo(short doType, int value)
    {
        return IsConnected
            && doType >= 0
            && Call(() => LTDMC.dmc_write_outport(_cardNo, (ushort)doType, unchecked((uint)value)));
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        // Debug grid: doType = port, doIndex = 0-based bit within the 32-bit port.
        if (!IsConnected || doType < 0 || doIndex < 0 || doIndex > 31)
        {
            return false;
        }

        var bitno = (ushort)(doType * 32 + doIndex);
        return WriteOutBit(bitno, value);
    }

    // ──────────────────────────────────────────────
    //  Axis Servo
    // ──────────────────────────────────────────────

    public bool EnableAxis(short axis)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        var level = _sevonActiveLow ? (ushort)0 : (ushort)1;
        var ok = Call(() => LTDMC.dmc_write_sevon_pin(_cardNo, (ushort)axis, level));
        if (ok)
        {
            _axisEnabled[axis] = true;
        }

        return ok;
    }

    public bool DisableAxis(short axis)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        var level = _sevonActiveLow ? (ushort)1 : (ushort)0;
        var ok = Call(() => LTDMC.dmc_write_sevon_pin(_cardNo, (ushort)axis, level));
        if (ok)
        {
            _axisEnabled[axis] = false;
        }

        return ok;
    }

    public bool IsAxisEnabled(short axis) => _axisEnabled.GetOrAdd(axis, false);

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        status = unchecked((int)LTDMC.dmc_axis_io_status(_cardNo, (ushort)axis));
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisState(short axis, out AxisStatus status)
    {
        status = default;
        if (!TryGetAxisStatus(axis, out var native))
        {
            return false;
        }

        var word = unchecked((uint)native);
        var servoOn = IsAxisEnabled(axis);
        var pin = LTDMC.dmc_read_sevon_pin(_cardNo, (ushort)axis);
        if (pin >= 0)
        {
            servoOn = _sevonActiveLow ? pin == 0 : pin != 0;
            _axisEnabled[axis] = servoOn;
        }

        var done = LTDMC.dmc_check_done(_cardNo, (ushort)axis);
        var moving = done == 0;

        TryGetAxisPrfPosition(axis, out var prf);
        TryGetAxisEncPosition(axis, out var enc);
        TryGetAxisVelocity(axis, out var vel);

        status = AxisStatus.Create(
            alarm: (word & DmcIoMap.AxisAlm) != 0,
            positiveLimit: (word & DmcIoMap.AxisElP) != 0,
            negativeLimit: (word & DmcIoMap.AxisElN) != 0,
            servoOn: servoOn,
            moving: moving,
            inPosition: (word & DmcIoMap.AxisInp) != 0,
            home: (word & DmcIoMap.AxisOrg) != 0,
            prfPosition: prf,
            encPosition: enc,
            velocity: vel);
        return true;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        position = LTDMC.dmc_get_position(_cardNo, (ushort)axis);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        position = LTDMC.dmc_get_encoder(_cardNo, (ushort)axis);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        velocity = LTDMC.dmc_read_current_speed(_cardNo, (ushort)axis);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool SetAxisPosition(short axis, double position)
    {
        return IsConnected
            && axis >= 0
            && Call(() => LTDMC.dmc_set_position(_cardNo, (ushort)axis, (int)position));
    }

    public bool SetAxisVelocity(short axis, double velocity)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        Motion(axis).Vel = velocity;
        return ApplyProfile(axis);
    }

    public bool SetAxisAcceleration(short axis, double acceleration)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        Motion(axis).Acc = acceleration;
        return ApplyProfile(axis);
    }

    public bool SetAxisDeceleration(short axis, double deceleration)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        Motion(axis).Dec = deceleration;
        return ApplyProfile(axis);
    }

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        var prm = Motion(axis);
        prm.Vel = velocity;
        prm.Acc = acceleration;
        prm.Dec = deceleration;
        return ApplyProfile(axis)
            && Call(() => LTDMC.dmc_pmove(_cardNo, (ushort)axis, targetPosition, posi_mode: 1));
    }

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        var prm = Motion(axis);
        prm.Vel = Math.Abs(velocity);
        prm.Acc = acceleration;
        prm.Dec = deceleration;
        var dir = (ushort)(velocity >= 0 ? 1 : 0);
        return ApplyProfile(axis)
            && Call(() => LTDMC.dmc_vmove(_cardNo, (ushort)axis, dir));
    }

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected || axis < 0)
        {
            return false;
        }

        var prm = Motion(axis);
        prm.Vel = velocity;
        prm.Acc = acceleration;
        prm.Dec = deceleration;
        var dir = (ushort)(velocity >= 0 ? 1 : 0);
        return ApplyProfile(axis)
            && Call(() => LTDMC.dmc_set_homemode(_cardNo, (ushort)axis, dir, Math.Abs(velocity), (ushort)homeMode, 0))
            && Call(() => LTDMC.dmc_home_move(_cardNo, (ushort)axis));
    }

    public bool Stop(int axisMask, int option = 0)
    {
        if (!IsConnected)
        {
            return false;
        }

        var stopMode = (ushort)(option != 0 ? 1 : 0);
        var ok = true;
        for (short axis = 0; axis < 32; axis++)
        {
            if ((axisMask & (1 << axis)) == 0)
            {
                continue;
            }

            ok &= Call(() => LTDMC.dmc_stop(_cardNo, (ushort)axis, stopMode));
        }

        return ok;
    }

    public void Dispose()
    {
        if (IsConnected)
        {
            ReleaseBoard();
        }

        IsConnected = false;
        _memory.Clear();
        _axisEnabled.Clear();
        _motion.Clear();
    }

    // ──────────────────────────────────────────────
    //  Address IO
    // ──────────────────────────────────────────────

    private bool TryReadIoAddress(string address, out object? value)
    {
        value = null;
        if (!DriverIoAddress.TryParse(address, out var io))
        {
            return false;
        }

        if (DmcIoMap.IsGeneral(io.Type))
        {
            if (io.IsBit)
            {
                if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var bitno))
                {
                    return false;
                }

                var state = io.IsOutput
                    ? LTDMC.dmc_read_outbit(_cardNo, bitno)
                    : LTDMC.dmc_read_inbit(_cardNo, bitno);
                _memory["driver.lastCode"] = state;
                if (state < 0)
                {
                    return false;
                }

                value = state != 0;
                return true;
            }

            if (io.IsOutput)
            {
                if (!TryReadDo(0, out var doWord))
                {
                    return false;
                }

                value = doWord;
                return true;
            }

            if (!TryReadDi(0, out var diWord))
            {
                return false;
            }

            value = diWord;
            return true;
        }

        if (io.IsBit && DmcIoMap.TryAxisStatusMask(io.Type, out var mask))
        {
            if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var axis))
            {
                return false;
            }

            if (!TryGetAxisStatus((short)axis, out var status))
            {
                return false;
            }

            value = (unchecked((uint)status) & mask) != 0;
            return true;
        }

        if (io.IsBit && DmcIoMap.IsServoEnable(io.Type))
        {
            if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var axis))
            {
                return false;
            }

            var pin = LTDMC.dmc_read_sevon_pin(_cardNo, axis);
            _memory["driver.lastCode"] = pin;
            if (pin < 0)
            {
                return false;
            }

            var enabled = _sevonActiveLow ? pin == 0 : pin != 0;
            value = enabled;
            return true;
        }

        return false;
    }

    private bool TryWriteIoAddress(string address, object? value)
    {
        if (!DriverIoAddress.TryParse(address, out var io) || !io.IsOutput)
        {
            return false;
        }

        if (DmcIoMap.IsGeneral(io.Type))
        {
            if (io.IsBit)
            {
                if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var bitno))
                {
                    return false;
                }

                return WriteOutBit(bitno, Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture));
            }

            if (value is bool || !TryConvertToInt(value, out var word))
            {
                return false;
            }

            return WriteDo(0, word);
        }

        if (io.IsBit && DmcIoMap.IsServoEnable(io.Type))
        {
            if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var axis))
            {
                return false;
            }

            var on = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            return on ? EnableAxis((short)axis) : DisableAxis((short)axis);
        }

        if (io.IsBit && DmcIoMap.IsAlarmClear(io.Type))
        {
            if (!DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var axis))
            {
                return false;
            }

            var pulse = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            var level = pulse ? (ushort)0 : (ushort)1;
            return Call(() => LTDMC.dmc_write_erc_pin(_cardNo, axis, level));
        }

        return false;
    }

    private bool WriteOutBit(ushort bitno, bool value)
    {
        var onOff = value ? (ushort)1 : (ushort)0;
        return Call(() => LTDMC.dmc_write_outbit(_cardNo, bitno, onOff));
    }

    private bool TryReadAxisAddress(string address, out object? value)
    {
        value = null;
        if (!TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            return false;
        }

        var suffix = ExtractSuffixAfterIndex(address, "axis.");
        if (suffix is null)
        {
            return TryGetAxisPrfPosition(axis, out var pos) && Assign(out value, pos);
        }

        if (suffix.Equals("enc", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAxisEncPosition(axis, out var enc) && Assign(out value, enc);
        }

        if (suffix.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAxisStatus(axis, out var status) && Assign(out value, status);
        }

        if (suffix.Equals("vel", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAxisVelocity(axis, out var vel) && Assign(out value, vel);
        }

        if (suffix.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            value = IsAxisEnabled(axis);
            return true;
        }

        return false;
    }

    private bool TryWriteAxisAddress(string address, object? value)
    {
        if (!TryParseTypeAndIndex(address, "axis.", out var axis) || !TryConvertToInt(value, out var target))
        {
            return false;
        }

        var prm = Motion(axis);
        return MoveAxisTrap(axis, target, prm.Vel, prm.Acc, prm.Dec);
    }

    private bool ApplyProfile(short axis)
    {
        var prm = Motion(axis);
        var vel = Math.Max(1, Math.Abs(prm.Vel));
        var tacc = ToRampTime(vel, prm.Acc);
        var tdec = ToRampTime(vel, prm.Dec);
        return Call(() => LTDMC.dmc_set_profile(_cardNo, (ushort)axis, 0, vel, tacc, tdec, 0));
    }

    private AxisMotionPrm Motion(short axis) =>
        _motion.GetOrAdd(axis, _ => new AxisMotionPrm());

    private bool Call(Func<short> invoke)
    {
        var rc = invoke();
        _memory["driver.lastCode"] = rc;
        return rc == 0;
    }

    private void ReleaseBoard()
    {
        if (!_ownsBoard)
        {
            return;
        }

        lock (BoardLock)
        {
            BoardRefCount = Math.Max(0, BoardRefCount - 1);
            if (BoardRefCount == 0)
            {
                try
                {
                    _ = LTDMC.dmc_board_close();
                }
                catch (DllNotFoundException)
                {
                }
            }
        }

        _ownsBoard = false;
    }

    private static double ToRampTime(double velocity, double accel)
    {
        if (accel <= 0)
        {
            return 0.1;
        }

        return Math.Clamp(velocity / accel, 0.001, 10);
    }

    private static bool Assign(out object? value, object raw)
    {
        value = raw;
        return true;
    }

    private static ushort GetUShort(MdkSetting.DriverConfig config, string key, ushort defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw)
            || !ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return defaultValue;
        }

        return value;
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
        var dotIdx = suffix.IndexOf('.');
        var numPart = dotIdx >= 0 ? suffix[..dotIdx] : suffix;
        return short.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string? ExtractSuffixAfterIndex(string address, string prefix)
    {
        var rest = address[prefix.Length..];
        var dotIdx = rest.IndexOf('.');
        if (dotIdx < 0)
        {
            return null;
        }

        var indexPart = rest[..dotIdx];
        if (!short.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return rest[(dotIdx + 1)..];
    }

    private sealed class AxisMotionPrm
    {
        public double Vel { get; set; } = 1000;
        public double Acc { get; set; } = 10000;
        public double Dec { get; set; } = 10000;
    }
}
