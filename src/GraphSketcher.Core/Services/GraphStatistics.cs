using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

public sealed record LinearRegressionResult(
    double Slope,
    double Intercept,
    double RSquared,
    int SampleCount,
    double RootMeanSquareError)
{
    public double Predict(double x)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The predictor must be finite.");
        }

        return Intercept + (Slope * x);
    }
}

/// <summary>
/// Performs statistical calculations used by graph analysis tools.
/// </summary>
public static class GraphStatistics
{
    public static LinearRegressionResult LinearRegression(IEnumerable<DataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var observations = points.ToArray();
        if (observations.Length < 2)
        {
            throw new ArgumentException(
                "Linear regression requires at least two points.",
                nameof(points));
        }

        var count = 0;
        var meanX = 0d;
        var meanY = 0d;
        var sumSquaredX = 0d;
        var sumProducts = 0d;
        var sumSquaredY = 0d;

        foreach (var point in observations)
        {
            ArgumentNullException.ThrowIfNull(point);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new ArgumentException(
                    "Regression points must contain finite X and Y values.",
                    nameof(points));
            }

            count++;
            var deltaX = point.X - meanX;
            var deltaY = point.Y - meanY;
            meanX += deltaX / count;
            meanY += deltaY / count;
            sumSquaredX += deltaX * (point.X - meanX);
            sumSquaredY += deltaY * (point.Y - meanY);
            sumProducts += deltaX * (point.Y - meanY);
        }

        if (!double.IsFinite(sumSquaredX) || sumSquaredX <= 0)
        {
            throw new ArgumentException(
                "Linear regression requires at least two distinct X values.",
                nameof(points));
        }

        var slope = sumProducts / sumSquaredX;
        var intercept = meanY - (slope * meanX);
        if (!double.IsFinite(slope) || !double.IsFinite(intercept))
        {
            throw new ArgumentException(
                "The point magnitudes are too large to calculate a finite regression.",
                nameof(points));
        }

        var sumSquaredError = 0d;
        foreach (var point in observations)
        {
            var residual = point.Y - (intercept + (slope * point.X));
            sumSquaredError += residual * residual;
        }

        var rSquared = CalculateRSquared(sumProducts, sumSquaredX, sumSquaredY);
        var rootMeanSquareError = Math.Sqrt(Math.Max(0, sumSquaredError) / count);
        return new LinearRegressionResult(
            slope,
            intercept,
            rSquared,
            count,
            rootMeanSquareError);
    }

    private static double CalculateRSquared(
        double sumProducts,
        double sumSquaredX,
        double sumSquaredY)
    {
        if (sumSquaredY <= 0)
        {
            return 1;
        }

        var correlation = (sumProducts / Math.Sqrt(sumSquaredX)) / Math.Sqrt(sumSquaredY);
        if (!double.IsFinite(correlation))
        {
            throw new ArgumentException(
                "The point magnitudes are too large to calculate a finite coefficient of determination.");
        }

        return Math.Clamp(correlation * correlation, 0, 1);
    }
}
