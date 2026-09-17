using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tests.Tools;

public sealed class CalibTransformTests
{
    [Fact]
    public void Affine_recovers_scale_and_translation()
    {
        // platform = 0.1 * pixel + offset
        var samples = new List<CalibTransform.PointPair>();
        for (var r = -1; r <= 1; r++)
        {
            for (var c = -1; c <= 1; c++)
            {
                var x = c * 5.0;
                var y = r * 5.0;
                samples.Add(new CalibTransform.PointPair(x * 10, y * 10, x, y));
            }
        }

        var fit = CalibTransform.Fit(samples, CalibTransform.Mode.Affine);
        Assert.True(fit.Residual < 1e-6, $"residual={fit.Residual}");
        Assert.Equal(0.1, fit.Matrix[0, 0], 5);
        Assert.Equal(0.1, fit.Matrix[1, 1], 5);
        Assert.Contains(';', fit.ToMatrixString());
    }

    [Fact]
    public void Rigid_recovers_rotation_and_translation()
    {
        var angle = Math.PI / 6;
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var tx = 2.5;
        var ty = -1.0;
        var samples = new List<CalibTransform.PointPair>();
        for (var i = 0; i < 9; i++)
        {
            var u = (i % 3) - 1.0;
            var v = (i / 3) - 1.0;
            var x = c * u - s * v + tx;
            var y = s * u + c * v + ty;
            samples.Add(new CalibTransform.PointPair(u, v, x, y));
        }

        var fit = CalibTransform.Fit(samples, CalibTransform.Mode.Rigid);
        Assert.True(fit.Residual < 1e-6, $"residual={fit.Residual}");
        Assert.Equal(c, fit.Matrix[0, 0], 5);
        Assert.Equal(tx, fit.Matrix[0, 2], 5);
        Assert.Equal(ty, fit.Matrix[1, 2], 5);
    }

    [Fact]
    public void Perspective_maps_known_homography()
    {
        // Mild perspective: platform ≈ affine with small projective terms
        var samples = new List<CalibTransform.PointPair>();
        double[,] h =
        {
            { 0.1, 0.01, 1 },
            { -0.02, 0.1, 2 },
            { 0.001, -0.001, 1 },
        };
        for (var r = -1; r <= 1; r++)
        {
            for (var c = -1; c <= 1; c++)
            {
                var u = c * 10.0;
                var v = r * 10.0;
                var (x, y) = CalibTransform.Apply(h, u, v);
                samples.Add(new CalibTransform.PointPair(u, v, x, y));
            }
        }

        var fit = CalibTransform.Fit(samples, CalibTransform.Mode.Perspective);
        Assert.True(fit.Residual < 1e-4, $"residual={fit.Residual}");
        var roundTrip = CalibTransform.Apply(fit.Matrix, 5, -5);
        var expected = CalibTransform.Apply(h, 5, -5);
        Assert.Equal(expected.X, roundTrip.X, 3);
        Assert.Equal(expected.Y, roundTrip.Y, 3);
    }

    [Theory]
    [InlineData("affine", CalibTransform.Mode.Affine)]
    [InlineData("rigid", CalibTransform.Mode.Rigid)]
    [InlineData("perspective", CalibTransform.Mode.Perspective)]
    [InlineData("刚体", CalibTransform.Mode.Rigid)]
    public void ParseMode_aliases(string raw, CalibTransform.Mode expected) =>
        Assert.Equal(expected, CalibTransform.ParseMode(raw));
}
