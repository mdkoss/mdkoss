using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Sample.SampleExt;

/// <summary>
/// Sample-owned software device (config type <c>samplebeacon</c>):
/// demonstrates registering a custom device + unified actions without board I/O.
/// </summary>
public sealed class SampleBeaconDevice : MDeviceBase
{
    private readonly object _lock = new();
    private int _pulseCount;
    private string _message;
    private string _label;

    public SampleBeaconDevice(string id, string name, IReadOnlyDictionary<string, string>? parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new SampleBeaconLogicalDriver(), vars)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _label = parameters.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label)
            ? label.Trim()
            : "Sample Beacon";
        _message = "ready";
        PublishUnlocked();
    }

    public string Label
    {
        get { lock (_lock) return _label; }
    }

    public int PulseCount
    {
        get { lock (_lock) return _pulseCount; }
    }

    public string Message
    {
        get { lock (_lock) return _message; }
    }

    public override void Initialize()
    {
        base.Initialize();
        lock (_lock)
        {
            _message = "initialized";
            PublishUnlocked();
        }
    }

    public override void Start()
    {
        State = MDeviceState.Running;
        WriteState("running");
        lock (_lock)
        {
            _message = "running";
            PublishUnlocked();
        }
    }

    public int Pulse(string? note = null)
    {
        lock (_lock)
        {
            _pulseCount++;
            _message = string.IsNullOrWhiteSpace(note) ? $"pulse #{_pulseCount}" : note.Trim();
            PublishUnlocked();
            return _pulseCount;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _pulseCount = 0;
            _message = "reset";
            PublishUnlocked();
        }
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "samplebeacon",
                State.ToString(),
                Driver.Name,
                Driver.IsConnected);
        }
    }

    private void PublishUnlocked()
    {
        Vars.Set(BuildVarKey("label"), _label);
        Vars.Set(BuildVarKey("pulseCount"), _pulseCount);
        Vars.Set(BuildVarKey("message"), _message);
        Vars.Set("sample.beacon.pulseCount", _pulseCount);
        Vars.Set("sample.beacon.message", _message);
        Vars.Set("sample.beacon.label", _label);
    }
}

internal static class SampleBeaconActions
{
    internal static DeviceActionResult Execute(
        SampleBeaconDevice device,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "pulse" => DeviceActionResult.Ok(new { pulseCount = device.Pulse(ReadNote(parameters)) }),
            "reset" => ExecuteReset(device),
            "status" => DeviceActionResult.Ok(new
            {
                label = device.Label,
                pulseCount = device.PulseCount,
                message = device.Message,
                state = device.State.ToString(),
            }),
            _ => DeviceActionResult.Fail("unknown_action"),
        };
    }

    private static DeviceActionResult ExecuteReset(SampleBeaconDevice device)
    {
        device.Reset();
        return DeviceActionResult.Ok(new { pulseCount = device.PulseCount });
    }

    private static string? ReadNote(Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("note", out var note))
        {
            return null;
        }

        return note.ValueKind == JsonValueKind.String ? note.GetString() : note.ToString();
    }
}

/// <summary>Always-connected logical driver for the software beacon device.</summary>
internal sealed class SampleBeaconLogicalDriver : IDriver
{
    public string Name => "SAMPLE-BEACON";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config) { }

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

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose() { }
}
