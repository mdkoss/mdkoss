using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Pnp;

/// <summary>Logical tray / nest map used by the PNP machine type.</summary>
public sealed class TrayDevice : MDeviceBase
{
    private readonly object _sync = new();
    private int _currentIndex;

    public TrayDevice(string id, string name, TrayDeviceParameters parameters, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.Generic, driver, vars)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Capacity = Math.Max(1, parameters.Rows * parameters.Cols);
        _currentIndex = Math.Clamp(parameters.StartIndex, 0, Capacity - 1);
        PublishVars();
    }

    public TrayDeviceParameters Parameters { get; }

    public int Capacity { get; }

    public int CurrentIndex
    {
        get { lock (_sync) return _currentIndex; }
    }

    public int Rows => Parameters.Rows;

    public int Cols => Parameters.Cols;

    public bool IsExhausted
    {
        get
        {
            lock (_sync)
            {
                return _currentIndex >= Capacity;
            }
        }
    }

    public bool TryGetCurrentNest(out TrayNestPose pose)
    {
        lock (_sync)
        {
            if (_currentIndex < 0 || _currentIndex >= Capacity)
            {
                pose = default;
                return false;
            }

            pose = GetNestPoseUnlocked(_currentIndex);
            return true;
        }
    }

    public TrayNestPose GetNestPose(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return GetNestPoseUnlocked(index);
        }
    }

    public bool Advance()
    {
        lock (_sync)
        {
            if (_currentIndex >= Capacity)
            {
                PublishVarsUnlocked();
                return false;
            }

            _currentIndex++;
            PublishVarsUnlocked();
            return _currentIndex < Capacity;
        }
    }

    public void Reset(int startIndex = 0)
    {
        lock (_sync)
        {
            _currentIndex = Math.Clamp(startIndex, 0, Capacity);
            PublishVarsUnlocked();
        }
    }

    public void MarkTrayChanged()
    {
        lock (_sync)
        {
            _currentIndex = 0;
            PublishVarsUnlocked();
            Vars.Set(BuildVarKey("lastTrayChangeUtc"), DateTime.UtcNow);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        PublishVars();
    }

    public override void Start()
    {
        // Logical device: no hard dependency on motion-card connection semantics.
        State = MDeviceState.Running;
        WriteState("running");
        PublishVars();
    }

    private TrayNestPose GetNestPoseUnlocked(int index)
    {
        var row = index / Parameters.Cols;
        var col = index % Parameters.Cols;
        var x = Parameters.OriginX + col * Parameters.PitchX;
        var y = Parameters.OriginY + row * Parameters.PitchY;
        return new TrayNestPose(index, row, col, x, y, Parameters.PickZ, Parameters.SafeZ);
    }

    private void PublishVars()
    {
        lock (_sync)
        {
            PublishVarsUnlocked();
        }
    }

    private void PublishVarsUnlocked()
    {
        Vars.Set(BuildVarKey("rows"), Parameters.Rows);
        Vars.Set(BuildVarKey("cols"), Parameters.Cols);
        Vars.Set(BuildVarKey("capacity"), Capacity);
        Vars.Set(BuildVarKey("currentIndex"), _currentIndex);
        Vars.Set(BuildVarKey("remaining"), Math.Max(0, Capacity - _currentIndex));
        Vars.Set(BuildVarKey("exhausted"), _currentIndex >= Capacity);
        Vars.Set(BuildVarKey("lastUpdateUtc"), DateTime.UtcNow);
    }
}

/// <summary>World pose of one nest cell in a tray.</summary>
public readonly record struct TrayNestPose(
    int Index,
    int Row,
    int Col,
    double X,
    double Y,
    double PickZ,
    double SafeZ);

/// <summary>Always-online placeholder driver for logical tray devices.</summary>
internal sealed class TrayLogicalDriver : IDriver
{
    public string Name => "TRAY";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config)
    {
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value) => false;

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        return false;
    }

    public bool WriteDo(short doType, int value) => false;

    public bool WriteDoBit(short doType, short doIndex, bool value) => false;

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

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration) => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose()
    {
    }
}
