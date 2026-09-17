using System.Globalization;
using System.Text;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>
/// Fits pixel→platform transforms used by nine-point calibration.
/// Modes: rigid (rotation+translation), affine (2×3), perspective (homography 3×3).
/// </summary>
public static class CalibTransform
{
    public enum Mode
    {
        Rigid,
        Affine,
        Perspective,
    }

    public readonly record struct PointPair(double PixelX, double PixelY, double PlatformX, double PlatformY);

    public sealed class FitResult
    {
        /// <summary>Row-major 3×3 matrix mapping [u v 1] → homogeneous platform.</summary>
        public double[,] Matrix { get; init; } = Identity();

        public double Residual { get; init; }

        public Mode Mode { get; init; }

        public string ToMatrixString()
        {
            var sb = new StringBuilder(96);
            for (var r = 0; r < 3; r++)
            {
                if (r > 0)
                {
                    sb.Append(';');
                }

                for (var c = 0; c < 3; c++)
                {
                    if (c > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(Matrix[r, c].ToString("G9", CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }
    }

    public static Mode ParseMode(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "rigid" or "刚体" or "r" => Mode.Rigid,
            "perspective" or "homography" or "透视" or "p" => Mode.Perspective,
            _ => Mode.Affine,
        };
    }

    public static FitResult Fit(IReadOnlyList<PointPair> samples, Mode mode)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("没有采样点");
        }

        return mode switch
        {
            Mode.Rigid => FitRigid(samples),
            Mode.Perspective => FitPerspective(samples),
            _ => FitAffine(samples),
        };
    }

    public static (double X, double Y) Apply(double[,] m, double u, double v)
    {
        var x = m[0, 0] * u + m[0, 1] * v + m[0, 2];
        var y = m[1, 0] * u + m[1, 1] * v + m[1, 2];
        var w = m[2, 0] * u + m[2, 1] * v + m[2, 2];
        if (Math.Abs(w) > 1e-12)
        {
            x /= w;
            y /= w;
        }

        return (x, y);
    }

    public static double[,] Identity() => new double[,]
    {
        { 1.0, 0.0, 0.0 },
        { 0.0, 1.0, 0.0 },
        { 0.0, 0.0, 1.0 },
    };

    private static FitResult FitRigid(IReadOnlyList<PointPair> samples)
    {
        // Kabsch without scale: R, t such that platform ≈ R * pixel + t
        var n = samples.Count;
        double muU = 0, muV = 0, muX = 0, muY = 0;
        foreach (var sample in samples)
        {
            muU += sample.PixelX;
            muV += sample.PixelY;
            muX += sample.PlatformX;
            muY += sample.PlatformY;
        }

        muU /= n;
        muV /= n;
        muX /= n;
        muY /= n;

        // 2D Procrustes: platform ≈ R * pixel + t, R = [[c,-s],[s,c]]
        double dot = 0, cross = 0;
        foreach (var sample in samples)
        {
            var du = sample.PixelX - muU;
            var dv = sample.PixelY - muV;
            var dx = sample.PlatformX - muX;
            var dy = sample.PlatformY - muY;
            dot += du * dx + dv * dy;
            cross += du * dy - dv * dx;
        }

        var angle = Math.Atan2(cross, dot);
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var tx = muX - (cos * muU - sin * muV);
        var ty = muY - (sin * muU + cos * muV);

        var m = new[,]
        {
            { cos, -sin, tx },
            { sin, cos, ty },
            { 0.0, 0.0, 1.0 },
        };
        return new FitResult { Matrix = m, Mode = Mode.Rigid, Residual = Residual(samples, m) };
    }

    private static FitResult FitAffine(IReadOnlyList<PointPair> samples)
    {
        // Solve [u v 1] * A^T = [X Y]  → 6 unknowns via normal equations
        var ata = new double[6, 6];
        var atb = new double[6];
        foreach (var p in samples)
        {
            AccumulateAffineRow(ata, atb, p.PixelX, p.PixelY, p.PlatformX, 0);
            AccumulateAffineRow(ata, atb, p.PixelX, p.PixelY, p.PlatformY, 3);
        }

        var coeff = SolveLinear(ata, atb);
        var m = new[,]
        {
            { coeff[0], coeff[1], coeff[2] },
            { coeff[3], coeff[4], coeff[5] },
            { 0, 0, 1 },
        };
        return new FitResult { Matrix = m, Mode = Mode.Affine, Residual = Residual(samples, m) };
    }

    private static void AccumulateAffineRow(double[,] ata, double[] atb, double u, double v, double target, int offset)
    {
        Span<double> row = stackalloc double[6];
        row[offset] = u;
        row[offset + 1] = v;
        row[offset + 2] = 1;
        for (var i = 0; i < 6; i++)
        {
            atb[i] += row[i] * target;
            for (var j = 0; j < 6; j++)
            {
                ata[i, j] += row[i] * row[j];
            }
        }
    }

    private static FitResult FitPerspective(IReadOnlyList<PointPair> samples)
    {
        // DLT with h33 = 1: 8 unknowns
        if (samples.Count < 4)
        {
            throw new InvalidOperationException("透视变换至少需要 4 个点");
        }

        var ata = new double[8, 8];
        var atb = new double[8];
        foreach (var p in samples)
        {
            AccumulateHomographyRows(ata, atb, p.PixelX, p.PixelY, p.PlatformX, p.PlatformY);
        }

        var h = SolveLinear(ata, atb);
        var m = new[,]
        {
            { h[0], h[1], h[2] },
            { h[3], h[4], h[5] },
            { h[6], h[7], 1 },
        };
        return new FitResult { Matrix = m, Mode = Mode.Perspective, Residual = Residual(samples, m) };
    }

    private static void AccumulateHomographyRows(
        double[,] ata,
        double[] atb,
        double u,
        double v,
        double x,
        double y)
    {
        // x = (h0 u + h1 v + h2) / (h6 u + h7 v + 1)
        // → h0 u + h1 v + h2 - h6 u x - h7 v x = x
        Span<double> r0 = stackalloc double[8];
        Span<double> r1 = stackalloc double[8];
        r0[0] = u;
        r0[1] = v;
        r0[2] = 1;
        r0[6] = -u * x;
        r0[7] = -v * x;
        r1[3] = u;
        r1[4] = v;
        r1[5] = 1;
        r1[6] = -u * y;
        r1[7] = -v * y;

        AddOuter(ata, atb, r0, x);
        AddOuter(ata, atb, r1, y);
    }

    private static void AddOuter(double[,] ata, double[] atb, Span<double> row, double target)
    {
        for (var i = 0; i < row.Length; i++)
        {
            atb[i] += row[i] * target;
            for (var j = 0; j < row.Length; j++)
            {
                ata[i, j] += row[i] * row[j];
            }
        }
    }

    private static double Residual(IReadOnlyList<PointPair> samples, double[,] m)
    {
        var sum = 0.0;
        foreach (var s in samples)
        {
            var (x, y) = Apply(m, s.PixelX, s.PixelY);
            var dx = x - s.PlatformX;
            var dy = y - s.PlatformY;
            sum += dx * dx + dy * dy;
        }

        return Math.Sqrt(sum / samples.Count);
    }

    private static double[] SolveLinear(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                m[i, j] = a[i, j];
            }

            m[i, n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-14)
            {
                throw new InvalidOperationException("变换矩阵求解失败（采样点共线或退化）");
            }

            if (pivot != col)
            {
                for (var c = 0; c <= n; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }
            }

            var div = m[col, col];
            for (var c = col; c <= n; c++)
            {
                m[col, c] /= div;
            }

            for (var r = 0; r < n; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var factor = m[r, col];
                for (var c = col; c <= n; c++)
                {
                    m[r, c] -= factor * m[col, c];
                }
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = m[i, n];
        }

        return x;
    }
}
