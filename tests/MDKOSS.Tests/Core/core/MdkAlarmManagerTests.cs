using MDKOSS.Core;

namespace MDKOSS.Tests.Core;

public sealed class MdkAlarmManagerTests
{
    [Fact]
    public void Trigger_sets_alarms_key_flag_to_1()
    {
        var setting = new MdkSetting
        {
            Alarms =
            [
                new MdkSetting.AlarmConfig
                {
                    Key = "axis.x.limit",
                    Msg = "X 轴限位",
                    Code = "E1001",
                    Solution = "回退后复位",
                    Module = "motion",
                    Display = true,
                },
            ],
        };
        var vars = new MVarStore();
        var alarms = new MdkAlarmManager(setting, vars);

        Assert.True(alarms.Trigger("axis.x.limit", out var error));
        Assert.Null(error);
        Assert.Equal(1, vars.Get<int>("alarms.axis.x.limit"));
        Assert.Equal(1, vars.Get<int>(MdkAlarmManager.CountVarKey));
        Assert.False(string.IsNullOrWhiteSpace(vars.Get<string>(MdkAlarmManager.ActiveVarKey)));
        Assert.False(string.IsNullOrWhiteSpace(setting.Alarms[0].TriggerTime));
    }

    [Fact]
    public void Clear_sets_alarms_key_flag_to_0()
    {
        var setting = new MdkSetting
        {
            Alarms =
            [
                new MdkSetting.AlarmConfig { Key = "door.open", Msg = "安全门打开", Display = true },
            ],
        };
        var vars = new MVarStore();
        var alarms = new MdkAlarmManager(setting, vars);

        Assert.True(alarms.Trigger("door.open", out _));
        Assert.Equal(1, vars.Get<int>("alarms.door.open"));

        Assert.True(alarms.Clear("door.open", out var error));
        Assert.Null(error);
        Assert.Equal(0, vars.Get<int>("alarms.door.open"));
        Assert.Equal(0, vars.Get<int>(MdkAlarmManager.CountVarKey));
        Assert.Equal("", setting.Alarms[0].TriggerTime);
    }

    [Fact]
    public void ClearAll_resets_all_flag_vars()
    {
        var setting = new MdkSetting
        {
            Alarms =
            [
                new MdkSetting.AlarmConfig { Key = "a1", Msg = "A1" },
                new MdkSetting.AlarmConfig { Key = "a2", Msg = "A2" },
            ],
        };
        var vars = new MVarStore();
        var alarms = new MdkAlarmManager(setting, vars);

        Assert.True(alarms.Trigger("a1", out _));
        Assert.True(alarms.Trigger("a2", out _));
        alarms.ClearAll();

        Assert.Equal(0, vars.Get<int>("alarms.a1"));
        Assert.Equal(0, vars.Get<int>("alarms.a2"));
        Assert.Equal(0, vars.Get<int>(MdkAlarmManager.CountVarKey));
    }

    [Fact]
    public void Trigger_unknown_key_fails_without_adhoc()
    {
        var alarms = new MdkAlarmManager(new MdkSetting(), new MVarStore());
        Assert.False(alarms.Trigger("missing.key", out var error));
        Assert.Equal("alarm_not_found", error);
    }
}
