using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Drivers.Boards;

/// <summary>
/// Catalog card driver: <c>simulate=true</c> uses memory; otherwise the vendor P/Invoke backend.
/// </summary>
public sealed class BoardCardDriver : IDriver
{
    private readonly BoardKind _kind;
    private IDriver _inner;

    public BoardCardDriver(BoardKind kind)
    {
        _kind = kind;
        _inner = new SimulatedCardDriver(kind);
    }

    public string Name => _inner.Name;

    public bool IsConnected => _inner.IsConnected;

    public void Initialize(MdkSetting.DriverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _inner.Dispose();
        _inner = IsSimulate(config) ? new SimulatedCardDriver(_kind) : new NativeCardDriver(_kind);
        _inner.Initialize(config);
    }

    public bool TryRead(string address, out object? value) => _inner.TryRead(address, out value);

    public bool Write(string address, object? value) => _inner.Write(address, value);

    public bool TryReadDi(short diType, out int value) => _inner.TryReadDi(diType, out value);

    public bool TryReadDo(short doType, out int value) => _inner.TryReadDo(doType, out value);

    public bool WriteDo(short doType, int value) => _inner.WriteDo(doType, value);

    public bool WriteDoBit(short doType, short doIndex, bool value) => _inner.WriteDoBit(doType, doIndex, value);

    public bool EnableAxis(short axis) => _inner.EnableAxis(axis);

    public bool DisableAxis(short axis) => _inner.DisableAxis(axis);

    public bool IsAxisEnabled(short axis) => _inner.IsAxisEnabled(axis);

    public bool TryGetAxisStatus(short axis, out int status) => _inner.TryGetAxisStatus(axis, out status);

    public bool TryGetAxisPrfPosition(short axis, out double position) => _inner.TryGetAxisPrfPosition(axis, out position);

    public bool TryGetAxisEncPosition(short axis, out double position) => _inner.TryGetAxisEncPosition(axis, out position);

    public bool TryGetAxisVelocity(short axis, out double velocity) => _inner.TryGetAxisVelocity(axis, out velocity);

    public bool SetAxisPosition(short axis, double position) => _inner.SetAxisPosition(axis, position);

    public bool SetAxisVelocity(short axis, double velocity) => _inner.SetAxisVelocity(axis, velocity);

    public bool SetAxisAcceleration(short axis, double acceleration) => _inner.SetAxisAcceleration(axis, acceleration);

    public bool SetAxisDeceleration(short axis, double deceleration) => _inner.SetAxisDeceleration(axis, deceleration);

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => _inner.MoveAxisTrap(axis, targetPosition, velocity, acceleration, deceleration);

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
        => _inner.MoveAxisJog(axis, velocity, acceleration, deceleration);

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => _inner.MoveAxisHome(axis, homeMode, velocity, acceleration, deceleration);

    public bool Stop(int axisMask, int option = 0) => _inner.Stop(axisMask, option);

    public bool MoveLine(short[] axes, double[] targets, double velocity, double acceleration, double deceleration, short crd = 0)
        => _inner.MoveLine(axes, targets, velocity, acceleration, deceleration, crd);

    public bool MoveArc(
        short[] axes,
        double[] targets,
        double[] center,
        bool clockwise,
        double velocity,
        double acceleration,
        double deceleration,
        short crd = 0)
        => _inner.MoveArc(axes, targets, center, clockwise, velocity, acceleration, deceleration, crd);

    public void Dispose() => _inner.Dispose();

    private static bool IsSimulate(MdkSetting.DriverConfig config)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue("simulate", out var raw))
        {
            return true;
        }

        return raw.Trim() switch
        {
            "0" or "false" or "False" or "no" => false,
            _ => true,
        };
    }
}
