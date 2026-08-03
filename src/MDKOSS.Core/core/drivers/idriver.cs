using MDKOSS.Core;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Unified abstraction for hardware motion-control drivers.
/// Covers: connection, IO, servo, axis status, position, and motion control.
/// </summary>
public interface IDriver : IDisposable
{
    // ──────────────────────────────────────────────
    //  Connection & Generic Access
    // ──────────────────────────────────────────────

    /// <summary>Driver type/name for diagnostics.</summary>
    string Name { get; }

    /// <summary>Indicates current link state.</summary>
    bool IsConnected { get; }

    /// <summary>Initializes driver with parsed config.</summary>
    void Initialize(MdkSetting.DriverConfig config);

    /// <summary>Reads a value from an address/key.</summary>
    bool TryRead(string address, out object? value);

    /// <summary>Writes a value to an address/key.</summary>
    bool Write(string address, object? value);

    // ──────────────────────────────────────────────
    //  IO Control
    // ──────────────────────────────────────────────

    /// <summary>Reads DI group value.</summary>
    bool TryReadDi(short diType, out int value);

    /// <summary>Reads DO group value.</summary>
    bool TryReadDo(short doType, out int value);

    /// <summary>Writes DO group value.</summary>
    bool WriteDo(short doType, int value);

    /// <summary>Writes DO bit value.</summary>
    bool WriteDoBit(short doType, short doIndex, bool value);

    // ──────────────────────────────────────────────
    //  Axis Servo Control
    // ──────────────────────────────────────────────

    /// <summary>Turns servo axis on.</summary>
    bool EnableAxis(short axis);

    /// <summary>Turns servo axis off.</summary>
    bool DisableAxis(short axis);

    /// <summary>Returns whether the axis servo is currently enabled.</summary>
    bool IsAxisEnabled(short axis);

    // ──────────────────────────────────────────────
    //  Axis Status
    // ──────────────────────────────────────────────

    /// <summary>Reads axis status word (driver-specific bit flags: ready, moving, in-position, alarm, etc.).</summary>
    bool TryGetAxisStatus(short axis, out int status);

    /// <summary>Reads current profile (command) position of axis.</summary>
    bool TryGetAxisPrfPosition(short axis, out double position);

    /// <summary>Reads current encoder (actual) position of axis.</summary>
    bool TryGetAxisEncPosition(short axis, out double position);

    /// <summary>Reads current velocity of axis (units/s).</summary>
    bool TryGetAxisVelocity(short axis, out double velocity);

    // ──────────────────────────────────────────────
    //  Position Setting
    // ──────────────────────────────────────────────

    /// <summary>Sets the profile position counter of the axis (for zeroing / offset).</summary>
    bool SetAxisPosition(short axis, double position);

    // ──────────────────────────────────────────────
    //  Motion Parameters
    // ──────────────────────────────────────────────

    /// <summary>Sets the target velocity for an axis.</summary>
    bool SetAxisVelocity(short axis, double velocity);

    /// <summary>Sets the acceleration for an axis.</summary>
    bool SetAxisAcceleration(short axis, double acceleration);

    /// <summary>Sets the deceleration for an axis.</summary>
    bool SetAxisDeceleration(short axis, double deceleration);

    // ──────────────────────────────────────────────
    //  Motion Control
    // ──────────────────────────────────────────────

    /// <summary>Executes trap (point-to-point) move on a single axis.</summary>
    bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration);

    /// <summary>Starts jog (continuous) move on a single axis. Call <see cref="Stop"/> to end.</summary>
    bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration);

    /// <summary>Starts home return on a single axis.</summary>
    bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration);

    /// <summary>Stops axis(es) by bitmask.</summary>
    bool Stop(int axisMask, int option = 0);
}
