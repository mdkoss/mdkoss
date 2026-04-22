using MDKOSS.Core;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Unified abstraction for hardware drivers.
/// </summary>
public interface IDriver : IDisposable
{
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

    /// <summary>Reads DI group value.</summary>
    bool TryReadDi(short diType, out int value);

    /// <summary>Reads DO group value.</summary>
    bool TryReadDo(short doType, out int value);

    /// <summary>Writes DO group value.</summary>
    bool WriteDo(short doType, int value);

    /// <summary>Writes DO bit value.</summary>
    bool WriteDoBit(short doType, short doIndex, bool value);

    /// <summary>Turns servo axis on.</summary>
    bool EnableAxis(short axis);

    /// <summary>Turns servo axis off.</summary>
    bool DisableAxis(short axis);

    /// <summary>Stops axis by mask.</summary>
    bool Stop(int axisMask, int option = 0);

    /// <summary>Reads current profile position of axis.</summary>
    bool TryGetAxisPrfPosition(short axis, out double position);

    /// <summary>Executes trap move on a single axis.</summary>
    bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration);
}
