using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>Moves an axis to a target and records encoder − expected as offset.</summary>
public sealed class AxisOffsetCalibTask : CalibMotionTaskBase
{
    private enum StepId
    {
        Enable,
        Move,
        Sample,
        Finish,
    }

    private StepId _step = StepId.Enable;
    private int _dwell;

    public AxisOffsetCalibTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null,
        MdkAlarmManager? alarms = null)
        : base(name, intervalMs, driver, vars, devices, parameters, alarms)
    {
    }

    protected override void OnStart()
    {
        _step = StepId.Enable;
        _dwell = 0;
        SetResult("ok", false);
        SetResult("offset", 0);
        SetResult("measured", 0);
        SetResult("expected", ReadDouble("expectedPos", 10));
        SetResult("message", "");
    }

    protected override void Step()
    {
        if (_dwell > 0)
        {
            _dwell--;
            return;
        }

        var axisId = ReadString("axisDeviceId", "axis-x");
        if (!TryGetAxisDevice(axisId, out var axis) || axis is null)
        {
            throw new InvalidOperationException($"轴 '{axisId}' 不存在");
        }

        var expected = ReadDouble("expectedPos", 10);

        switch (_step)
        {
            case StepId.Enable:
                if (!AxisSetMotionEnabled(axisId, true))
                {
                    throw new InvalidOperationException($"使能轴 '{axisId}' 失败");
                }

                Enter(CalibPhase.Running, $"使能 {axisId}，目标 {expected}");
                _step = StepId.Move;
                _dwell = 1;
                break;

            case StepId.Move:
                if (!AxisMoveTo(axisId, expected))
                {
                    throw new InvalidOperationException($"轴 '{axisId}' 运动失败");
                }

                Enter(CalibPhase.Running, $"运动到 {expected}");
                _step = StepId.Sample;
                _dwell = Math.Max(1, ReadInt("settleTicks", 3));
                break;

            case StepId.Sample:
                var measured = ReadAxisPosition(axis);
                var offset = measured - expected;
                SetResult("measured", measured);
                SetResult("expected", expected);
                SetResult("offset", offset);
                SetResult("ok", true);
                SetResult("message", $"offset={offset:F4}");
                Enter(CalibPhase.Running, $"采样完成 measured={measured:F4}");
                _step = StepId.Finish;
                break;

            case StepId.Finish:
                AxisSetMotionEnabled(axisId, false);
                Enter(CalibPhase.Done, "轴偏置标定完成");
                break;
        }
    }

    protected override void OnStop()
    {
        var axisId = ReadString("axisDeviceId", "axis-x");
        AxisStopMotion(axisId);
    }
}
