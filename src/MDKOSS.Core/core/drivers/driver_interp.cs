namespace MDKOSS.Core.Drivers;

/// <summary>
/// Shared validation and geometry for <see cref="IDriver"/> linear / circular interpolation.
/// </summary>
public static class DriverInterp
{
    public const int MinAxes = 2;
    public const int MaxAxes = 8;

    public static bool TryValidateLine(
        short[]? axes,
        double[]? targets,
        double velocity,
        double acceleration,
        double deceleration,
        out string? error)
    {
        if (!TryValidateGroup(axes, targets, velocity, acceleration, deceleration, out error))
        {
            return false;
        }

        return true;
    }

    public static bool TryValidateArc(
        short[]? axes,
        double[]? targets,
        double[]? center,
        double velocity,
        double acceleration,
        double deceleration,
        out string? error)
    {
        if (!TryValidateGroup(axes, targets, velocity, acceleration, deceleration, out error))
        {
            return false;
        }

        if (center is null || center.Length < 2)
        {
            error = "Arc center must have at least two coordinates.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Arc in the first two axes. Sweep is signed (negative = clockwise).
    /// Same start/end is treated as zero length, not a full circle.
    /// </summary>
    public static bool TryComputeArc(
        double x0,
        double y0,
        double x1,
        double y1,
        double cx,
        double cy,
        bool clockwise,
        out double radius,
        out double startAngle,
        out double sweep,
        out string? error)
    {
        radius = 0;
        startAngle = 0;
        sweep = 0;
        error = null;

        var dx0 = x0 - cx;
        var dy0 = y0 - cy;
        var dx1 = x1 - cx;
        var dy1 = y1 - cy;
        var r0 = Math.Sqrt((dx0 * dx0) + (dy0 * dy0));
        var r1 = Math.Sqrt((dx1 * dx1) + (dy1 * dy1));
        if (r0 < 1e-9)
        {
            error = "Arc start coincides with center.";
            return false;
        }

        var tol = Math.Max(1e-3, r0 * 1e-3);
        if (Math.Abs(r0 - r1) > tol)
        {
            error = "Arc end is not on the circle defined by start and center.";
            return false;
        }

        radius = r0;
        startAngle = Math.Atan2(dy0, dx0);
        if (Math.Abs(x1 - x0) < 1e-9 && Math.Abs(y1 - y0) < 1e-9)
        {
            sweep = 0;
            return true;
        }

        var endAngle = Math.Atan2(dy1, dx1);
        sweep = endAngle - startAngle;
        if (clockwise)
        {
            while (sweep > 0)
            {
                sweep -= 2 * Math.PI;
            }
        }
        else
        {
            while (sweep < 0)
            {
                sweep += 2 * Math.PI;
            }
        }

        return true;
    }

    public static double Distance(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var n = Math.Min(a.Length, b.Length);
        var sum = 0.0;
        for (var i = 0; i < n; i++)
        {
            var d = b[i] - a[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    public static bool OverlapsMask(short[] axes, int axisMask)
    {
        foreach (var axis in axes)
        {
            if (axis is >= 0 and <= 30 && (axisMask & (1 << axis)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryValidateGroup(
        short[]? axes,
        double[]? targets,
        double velocity,
        double acceleration,
        double deceleration,
        out string? error)
    {
        error = null;
        if (axes is null || targets is null || axes.Length != targets.Length)
        {
            error = "Axes and targets must be the same non-empty length.";
            return false;
        }

        if (axes.Length < MinAxes || axes.Length > MaxAxes)
        {
            error = $"Interpolation supports {MinAxes}..{MaxAxes} axes.";
            return false;
        }

        if (HasDuplicate(axes))
        {
            error = "Interpolation axes must be unique.";
            return false;
        }

        if (velocity <= 0 || acceleration <= 0 || deceleration <= 0)
        {
            error = "Velocity, acceleration and deceleration must be positive.";
            return false;
        }

        return true;
    }

    private static bool HasDuplicate(short[] axes)
    {
        for (var i = 0; i < axes.Length; i++)
        {
            for (var j = i + 1; j < axes.Length; j++)
            {
                if (axes[i] == axes[j])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
