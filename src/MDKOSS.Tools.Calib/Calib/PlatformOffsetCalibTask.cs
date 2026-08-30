using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>Enables a platform axis, moves to a target, and records encoder − expected as offset.</summary>
public sealed class PlatformOffsetCalibTask : CalibMotionTaskBase
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

    public PlatformOffsetCalibTask(
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

        var platformId = ReadString("platformDeviceId", "platform-xy");
        var axisLetter = ReadString("axisLetter", "X");
        if (!TryGetPlatformDevice(platformId, out var platform) || platform is null)
        {
            throw new InvalidOperationException($"平台 '{platformId}' 不存在");
        }

        var expected = ReadDouble("expectedPos", 10);

        switch (_step)
        {
            case StepId.Enable:
                if (!PlatformStartMotion(platformId))
                {
                    throw new InvalidOperationException($"使能平台 '{platformId}' 失败");
                }

                Enter(CalibPhase.Running, $"使能 {platformId}.{axisLetter}，目标 {expected}");
                _step = StepId.Move;
                _dwell = 1;
                break;

            case StepId.Move:
                if (!PlatformAxisMoveTo(platformId, axisLetter, expected))
                {
                    throw new InvalidOperationException($"平台 '{platformId}' 轴 {axisLetter} 运动失败");
                }

                Enter(CalibPhase.Running, $"运动到 {expected}");
                _step = StepId.Sample;
                _dwell = Math.Max(1, ReadInt("settleTicks", 3));
                break;

            case StepId.Sample:
                var measured = ReadObserved(platform, axisLetter, expected);
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
                PlatformStopMotion(platformId);
                Enter(CalibPhase.Done, "平台偏置标定完成");
                break;
        }
    }

    protected override void OnStop()
    {
        var platformId = ReadString("platformDeviceId", "platform-xy");
        PlatformStopMotion(platformId);
    }

    private double ReadObserved(PlatformDevice platform, string axisLetter, double commanded)
    {
        var entry = platform.Axes.FirstOrDefault(a =>
            string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
        if (entry?.Axis is not null)
        {
            var pos = ReadAxisPosition(entry.Axis);
            if (Math.Abs(pos) > 1e-12 || Math.Abs(commanded) < 1e-12)
            {
                return pos;
            }
        }

        return commanded;
    }
}
