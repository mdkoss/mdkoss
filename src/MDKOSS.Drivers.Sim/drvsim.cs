using System.Collections.Concurrent;
using System.Globalization;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Software simulation driver for controller development and testing.
/// Simulates motion-control operations in memory without hardware.
/// Digital <c>bit.{n}</c> numbering follows parameter <c>ioBitBase</c> (default 0).
/// Axis trap/jog/home advance on an internal <see cref="MotionTickMs"/> timer.
/// </summary>
public sealed class DrvSim : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<short, int> _di = new();
    private readonly ConcurrentDictionary<short, int> _do = new();
    private readonly ConcurrentDictionary<short, SimAxisState> _axes = new();
    private Timer? _motionTimer;
    private int _disposed;
    private short _ioBitBase;

    public const int MotionTickMs = 10;

    public string Name => "SIM";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        _ioBitBase = ParseIoBitBase(config);
        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.mode"] = "simulation";
        _memory["driver.ioBitBase"] = _ioBitBase;
        _memory["driver.lastCode"] = 0;
        IsConnected = true;
        StartMotionTimer();
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

        value = _di.GetOrAdd(diType, 0);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        value = _do.GetOrAdd(doType, 0);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool WriteDo(short doType, int value)
    {
        if (!IsConnected)
        {
            return false;
        }

        _do[doType] = value;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        // IDriver bit index is 0-based (debug grid). Address bit.{n} follows ioBitBase (default 0).
        if (!IsConnected || doIndex < 0 || doIndex > 31)
        {
            return false;
        }

        var current = _do.GetOrAdd(doType, 0);
        var bitMask = 1 << doIndex;
        var next = value ? (current | bitMask) : (current & ~bitMask);
        _do[doType] = next;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Axis Servo Control
    // ──────────────────────────────────────────────

    public bool EnableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            state.Enabled = true;
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool DisableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            state.Enabled = false;
            Halt(state);
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool IsAxisEnabled(short axis)
    {
        var state = Axis(axis);
        lock (state.Gate)
        {
            return state.Enabled;
        }
    }

    // ──────────────────────────────────────────────
    //  Axis Status
    // ──────────────────────────────────────────────

    public bool TryGetAxisStatus(short axis, out int status)
    {
        if (!TryGetAxisState(axis, out var state))
        {
            status = 0;
            return false;
        }

        status = state.Raw;
        return true;
    }

    public bool TryGetAxisState(short axis, out AxisStatus status)
    {
        status = default;
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            status = AxisStatus.Create(
                alarm: state.Alarm,
                servoOn: state.Enabled,
                moving: state.Moving,
                inPosition: !state.Moving,
                home: state.Homed,
                prfPosition: state.PrfPosition,
                encPosition: state.EncPosition,
                velocity: state.CurrentVelocity);
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            position = state.PrfPosition;
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            position = state.EncPosition;
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            velocity = state.CurrentVelocity;
        }

        _memory["driver.lastCode"] = 0;
        return true;
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

        var state = Axis(axis);
        lock (state.Gate)
        {
            Halt(state);
            state.PrfPosition = position;
            state.EncPosition = position;
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Motion Parameters
    // ──────────────────────────────────────────────

    public bool SetAxisVelocity(short axis, double velocity)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            state.CommandSpeed = Math.Abs(velocity);
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool SetAxisAcceleration(short axis, double acceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            state.Acceleration = Math.Abs(acceleration);
        }

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool SetAxisDeceleration(short axis, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            state.Deceleration = Math.Abs(deceleration);
        }

        _memory["driver.lastCode"] = 0;
        return true;
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

        var state = Axis(axis);
        lock (state.Gate)
        {
            if (!state.Enabled)
            {
                _memory["driver.lastCode"] = -1;
                _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
                return false;
            }

            state.Mode = SimMotionMode.Trap;
            state.TargetPosition = targetPosition;
            state.CommandSpeed = Math.Max(Math.Abs(velocity), 1e-6);
            state.Acceleration = Math.Max(Math.Abs(acceleration), 1e-6);
            state.Deceleration = Math.Max(Math.Abs(deceleration), 1e-6);
            state.Moving = true;
        }

        _memory[$"axis.{axis}.targetPosition"] = targetPosition;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            if (!state.Enabled)
            {
                _memory["driver.lastCode"] = -1;
                _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
                return false;
            }

            state.Mode = SimMotionMode.Jog;
            state.JogVelocity = velocity;
            state.CommandSpeed = Math.Abs(velocity);
            state.Acceleration = Math.Max(Math.Abs(acceleration), 1e-6);
            state.Deceleration = Math.Max(Math.Abs(deceleration), 1e-6);
            state.Moving = true;
        }

        _memory[$"axis.{axis}.jogVelocity"] = velocity;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = Axis(axis);
        lock (state.Gate)
        {
            if (!state.Enabled)
            {
                _memory["driver.lastCode"] = -1;
                _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
                return false;
            }

            state.Mode = SimMotionMode.Home;
            state.TargetPosition = 0;
            state.Homed = false;
            state.CommandSpeed = Math.Max(Math.Abs(velocity), 1e-6);
            state.Acceleration = Math.Max(Math.Abs(acceleration), 1e-6);
            state.Deceleration = Math.Max(Math.Abs(deceleration), 1e-6);
            state.Moving = Math.Abs(state.PrfPosition) > 1e-6 || Math.Abs(state.CurrentVelocity) > 1e-6;
            if (!state.Moving)
            {
                Arrive(state);
            }
        }

        _memory[$"axis.{axis}.homeMode"] = homeMode;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool Stop(int axisMask, int option = 0)
    {
        if (!IsConnected)
        {
            return false;
        }

        foreach (var kvp in _axes)
        {
            if (kvp.Key is < 0 or > 30 || (axisMask & (1 << kvp.Key)) == 0)
            {
                continue;
            }

            lock (kvp.Value.Gate)
            {
                if (option != 0)
                {
                    Halt(kvp.Value);
                }
                else if (kvp.Value.Mode != SimMotionMode.Idle)
                {
                    kvp.Value.Mode = SimMotionMode.Stopping;
                    kvp.Value.Moving = true;
                }
            }
        }

        _memory["motion.lastStopMask"] = axisMask;
        _memory["motion.lastStopOption"] = option;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IsConnected = false;
        var timer = _motionTimer;
        _motionTimer = null;
        timer?.Dispose();
        _memory.Clear();
        _di.Clear();
        _do.Clear();
        _axes.Clear();
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
            var bit = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            return io.IsOutput
                ? WritePortBit(_do, io.Type, io.BitIndex!.Value, bit)
                : WritePortBit(_di, io.Type, io.BitIndex!.Value, bit);
        }

        if (value is bool || !TryConvertToInt(value, out var word))
        {
            return false;
        }

        if (io.IsOutput)
        {
            return WriteDo(io.Type, word);
        }

        _di[io.Type] = word;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    private bool WritePortBit(ConcurrentDictionary<short, int> port, short type, short addressBit, bool value)
    {
        if (!IsConnected || !TryAddressBitShift(addressBit, out var shift))
        {
            return false;
        }

        var current = port.GetOrAdd(type, 0);
        port[type] = ApplyPortBit(current, shift, value);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    /// <summary>
    /// Maps address <c>bit.{n}</c> to a 0-based shift in the port word.
    /// <c>ioBitBase=0</c>: n=0 is the first bit; <c>ioBitBase=1</c>: n=1 is the first bit (GTS-style).
    /// </summary>
    private bool TryAddressBitShift(short addressBit, out int shift)
    {
        shift = addressBit - _ioBitBase;
        return shift is >= 0 and <= 31;
    }

    private static bool TestPortBit(int word, int shift) => (word & (1 << shift)) != 0;

    private static int ApplyPortBit(int word, int shift, bool value)
    {
        var mask = 1 << shift;
        return value ? word | mask : word & ~mask;
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

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;

        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
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
            if (TryGetAxisPrfPosition(axis, out var axisPos))
            {
                value = axisPos;
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

            return MoveAxisTrap(axis, target, 1000, 10000, 10000);
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  Motion timer (10 ms)
    // ──────────────────────────────────────────────

    private SimAxisState Axis(short axis) => _axes.GetOrAdd(axis, static _ => new SimAxisState());

    private void StartMotionTimer()
    {
        _motionTimer ??= new Timer(OnMotionTick, null, MotionTickMs, MotionTickMs);
    }

    private void OnMotionTick(object? _)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsConnected)
        {
            return;
        }

        var dt = MotionTickMs / 1000.0;
        foreach (var kv in _axes)
        {
            lock (kv.Value.Gate)
            {
                TickAxis(kv.Value, dt);
            }
        }
    }

    private static void TickAxis(SimAxisState state, double dt)
    {
        if (!state.Enabled)
        {
            Halt(state);
            return;
        }

        if (state.Mode == SimMotionMode.Idle)
        {
            state.CurrentVelocity = 0;
            state.Moving = false;
            return;
        }

        if (state.Mode == SimMotionMode.Jog)
        {
            state.CurrentVelocity = ApproachVelocity(
                state.CurrentVelocity, state.JogVelocity, state.Acceleration, state.Deceleration, dt);
            Integrate(state, dt);
            state.Moving = true;
            return;
        }

        if (state.Mode == SimMotionMode.Stopping)
        {
            state.CurrentVelocity = ApproachVelocity(
                state.CurrentVelocity, 0, state.Acceleration, state.Deceleration, dt);
            Integrate(state, dt);
            if (Math.Abs(state.CurrentVelocity) < 1e-6)
            {
                Halt(state);
            }

            return;
        }

        // Trap / Home: trapezoid toward TargetPosition.
        var remaining = state.TargetPosition - state.PrfPosition;
        if (Math.Abs(remaining) < 1e-6 && Math.Abs(state.CurrentVelocity) < 1e-6)
        {
            Arrive(state);
            return;
        }

        var dir = remaining >= 0 ? 1.0 : -1.0;
        var cruise = Math.Max(state.CommandSpeed, 1e-6);
        var dec = Math.Max(state.Deceleration, 1e-6);
        var v = state.CurrentVelocity;
        var toward = Math.Abs(v) < 1e-9 || Math.Sign(v) == Math.Sign(dir);
        var stopDist = (v * v) / (2.0 * dec);

        if (!toward)
        {
            state.CurrentVelocity = ApproachVelocity(v, 0, state.Acceleration, dec, dt);
        }
        else if (Math.Abs(remaining) <= stopDist)
        {
            state.CurrentVelocity = ApproachVelocity(v, 0, state.Acceleration, dec, dt);
        }
        else
        {
            state.CurrentVelocity = ApproachVelocity(v, dir * cruise, state.Acceleration, dec, dt);
        }

        var step = state.CurrentVelocity * dt;
        if (toward && Math.Abs(step) >= Math.Abs(remaining))
        {
            Arrive(state);
            return;
        }

        Integrate(state, dt);
        state.Moving = true;
    }

    private static void Integrate(SimAxisState state, double dt)
    {
        state.PrfPosition += state.CurrentVelocity * dt;
        state.EncPosition = state.PrfPosition;
    }

    private static void Arrive(SimAxisState state)
    {
        state.PrfPosition = state.TargetPosition;
        state.EncPosition = state.TargetPosition;
        state.CurrentVelocity = 0;
        state.Moving = false;
        if (state.Mode == SimMotionMode.Home)
        {
            state.Homed = true;
        }

        state.Mode = SimMotionMode.Idle;
    }

    private static void Halt(SimAxisState state)
    {
        state.Mode = SimMotionMode.Idle;
        state.Moving = false;
        state.CurrentVelocity = 0;
    }

    private static double ApproachVelocity(double current, double target, double acc, double dec, double dt)
    {
        var sameDir = Math.Abs(current) < 1e-12
            || Math.Sign(current) == Math.Sign(target)
            || Math.Abs(target) < 1e-12;
        var speedingUp = Math.Abs(target) > Math.Abs(current) && sameDir && Math.Abs(target) > 1e-12;
        var rate = Math.Max(speedingUp ? acc : dec, 1e-6);
        var delta = target - current;
        var maxStep = rate * dt;
        return Math.Abs(delta) <= maxStep ? target : current + Math.Sign(delta) * maxStep;
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

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

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            result = boolValue ? 1 : 0;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private enum SimMotionMode
    {
        Idle,
        Trap,
        Jog,
        Home,
        Stopping,
    }

    /// <summary>
    /// Simulated per-axis state stored in the concurrent dictionary.
    /// </summary>
    private sealed class SimAxisState
    {
        public readonly object Gate = new();
        public SimMotionMode Mode;
        public bool Enabled;
        public bool Moving;
        public bool Homed;
        public bool Alarm;
        public double PrfPosition;
        public double EncPosition;
        public double CurrentVelocity;
        public double CommandSpeed;
        public double JogVelocity;
        public double TargetPosition;
        public double Acceleration = 10000;
        public double Deceleration = 10000;
    }
}
