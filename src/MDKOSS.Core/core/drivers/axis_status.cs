namespace MDKOSS.Core.Drivers;

/// <summary>
/// GTS <c>GT_GetSts</c> bit masks (bit 0 reserved). Other drivers pack
/// <see cref="AxisStatus.Raw"/> with the same layout so callers can test flags portably.
/// Home is not in the status word; it is <see cref="AxisStatus.Home"/>.
/// </summary>
public static class AxisStatusBits
{
    public const int Alarm = 1 << 1;
    public const int FollowError = 1 << 2;
    public const int PositiveLimitLevel = 1 << 3;
    public const int PositiveLimit = 1 << 4;
    public const int NegativeLimit = 1 << 5;
    public const int SmoothStop = 1 << 6;
    public const int AbruptStop = 1 << 7;
    public const int ServoOn = 1 << 8;
    public const int Moving = 1 << 9;
    public const int InPosition = 1 << 10;

    public static bool Test(int word, int mask) => (word & mask) != 0;

    public static int Pack(
        bool alarm = false,
        bool followError = false,
        bool positiveLimitLevel = false,
        bool positiveLimit = false,
        bool negativeLimit = false,
        bool smoothStop = false,
        bool abruptStop = false,
        bool servoOn = false,
        bool moving = false,
        bool inPosition = false)
    {
        var raw = 0;
        if (alarm) raw |= Alarm;
        if (followError) raw |= FollowError;
        if (positiveLimitLevel) raw |= PositiveLimitLevel;
        if (positiveLimit) raw |= PositiveLimit;
        if (negativeLimit) raw |= NegativeLimit;
        if (smoothStop) raw |= SmoothStop;
        if (abruptStop) raw |= AbruptStop;
        if (servoOn) raw |= ServoOn;
        if (moving) raw |= Moving;
        if (inPosition) raw |= InPosition;
        return raw;
    }
}

/// <summary>
/// Complete per-axis snapshot. Flag names follow GTS <c>GT_GetSts</c>;
/// <see cref="Home"/> is the origin sensor (<c>GT_GetDi(MC_HOME)</c>).
/// </summary>
public readonly record struct AxisStatus
{
    /// <summary>GTS-layout status word (native <c>GT_GetSts</c> on GTS).</summary>
    public int Raw { get; init; }

    public bool Alarm { get; init; }
    public bool FollowError { get; init; }
    public bool PositiveLimitLevel { get; init; }
    public bool PositiveLimit { get; init; }
    public bool NegativeLimit { get; init; }
    public bool SmoothStop { get; init; }
    public bool AbruptStop { get; init; }
    public bool ServoOn { get; init; }
    public bool Moving { get; init; }
    public bool InPosition { get; init; }
    public bool Home { get; init; }

    public double PrfPosition { get; init; }
    public double EncPosition { get; init; }
    public double Velocity { get; init; }

    public static AxisStatus FromGts(
        int raw,
        bool home = false,
        double prfPosition = 0,
        double encPosition = 0,
        double velocity = 0) =>
        new()
        {
            Raw = raw,
            Alarm = AxisStatusBits.Test(raw, AxisStatusBits.Alarm),
            FollowError = AxisStatusBits.Test(raw, AxisStatusBits.FollowError),
            PositiveLimitLevel = AxisStatusBits.Test(raw, AxisStatusBits.PositiveLimitLevel),
            PositiveLimit = AxisStatusBits.Test(raw, AxisStatusBits.PositiveLimit),
            NegativeLimit = AxisStatusBits.Test(raw, AxisStatusBits.NegativeLimit),
            SmoothStop = AxisStatusBits.Test(raw, AxisStatusBits.SmoothStop),
            AbruptStop = AxisStatusBits.Test(raw, AxisStatusBits.AbruptStop),
            ServoOn = AxisStatusBits.Test(raw, AxisStatusBits.ServoOn),
            Moving = AxisStatusBits.Test(raw, AxisStatusBits.Moving),
            InPosition = AxisStatusBits.Test(raw, AxisStatusBits.InPosition),
            Home = home,
            PrfPosition = prfPosition,
            EncPosition = encPosition,
            Velocity = velocity,
        };

    public static AxisStatus Create(
        bool alarm = false,
        bool followError = false,
        bool positiveLimitLevel = false,
        bool positiveLimit = false,
        bool negativeLimit = false,
        bool smoothStop = false,
        bool abruptStop = false,
        bool servoOn = false,
        bool moving = false,
        bool inPosition = false,
        bool home = false,
        double prfPosition = 0,
        double encPosition = 0,
        double velocity = 0)
    {
        return new AxisStatus
        {
            Raw = AxisStatusBits.Pack(
                alarm,
                followError,
                positiveLimitLevel,
                positiveLimit,
                negativeLimit,
                smoothStop,
                abruptStop,
                servoOn,
                moving,
                inPosition),
            Alarm = alarm,
            FollowError = followError,
            PositiveLimitLevel = positiveLimitLevel,
            PositiveLimit = positiveLimit,
            NegativeLimit = negativeLimit,
            SmoothStop = smoothStop,
            AbruptStop = abruptStop,
            ServoOn = servoOn,
            Moving = moving,
            InPosition = inPosition,
            Home = home,
            PrfPosition = prfPosition,
            EncPosition = encPosition,
            Velocity = velocity,
        };
    }

    /// <summary>UI lamp definitions (code, title, reader, fault-when-on).</summary>
    public static readonly AxisStatusLamp[] Lamps =
    [
        new("ALM", "驱动器报警", static s => s.Alarm, Fault: true),
        new("FE", "跟随误差越限", static s => s.FollowError, Fault: true),
        new("PLL", "正限位电平", static s => s.PositiveLimitLevel, Fault: false),
        new("EL+", "正限位", static s => s.PositiveLimit, Fault: true),
        new("EL-", "负限位", static s => s.NegativeLimit, Fault: true),
        new("SSTP", "平滑停止", static s => s.SmoothStop, Fault: false),
        new("ESTP", "急停", static s => s.AbruptStop, Fault: true),
        new("SVON", "伺服使能", static s => s.ServoOn, Fault: false),
        new("MOVE", "规划运动", static s => s.Moving, Fault: false),
        new("INP", "电机到位", static s => s.InPosition, Fault: false),
        new("ORG", "原点", static s => s.Home, Fault: false),
    ];

    public string FormatFlags()
    {
        Span<char> buffer = stackalloc char[64];
        var n = 0;
        foreach (var lamp in Lamps)
        {
            if (lamp.Read(this))
            {
                Append(ref n, buffer, lamp.Code);
            }
        }

        return n == 0 ? "-" : new string(buffer[..n]);
    }

    private static void Append(ref int n, Span<char> buffer, string token)
    {
        if (n > 0)
        {
            buffer[n++] = ' ';
        }

        token.AsSpan().CopyTo(buffer[n..]);
        n += token.Length;
    }
}

/// <summary>One decoded axis-status lamp for debug / monitor UI.</summary>
public readonly record struct AxisStatusLamp(
    string Code,
    string Title,
    Func<AxisStatus, bool> Read,
    bool Fault);
