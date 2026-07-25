using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class GraphStatisticsTests
{
    [Fact]
    public void PerfectRegressionReturnsExpectedLineAndRSquared()
    {
        DataPoint[] points =
        [
            new(0, 1),
            new(1, 3),
            new(2, 5),
            new(3, 7),
        ];

        var result = GraphStatistics.LinearRegression(points);

        Assert.Equal(2, result.Slope, 12);
        Assert.Equal(1, result.Intercept, 12);
        Assert.Equal(1, result.RSquared, 12);
        Assert.Equal(4, result.SampleCount);
        Assert.Equal(0, result.RootMeanSquareError, 12);
        Assert.Equal(11, result.Predict(5), 12);
    }

    [Fact]
    public void NoisyRegressionCalculatesCoefficientOfDetermination()
    {
        DataPoint[] points = [new(1, 1), new(2, 2), new(3, 2)];

        var result = GraphStatistics.LinearRegression(points);

        Assert.Equal(0.5, result.Slope, 12);
        Assert.Equal(2d / 3, result.Intercept, 12);
        Assert.Equal(0.75, result.RSquared, 12);
        Assert.True(result.RootMeanSquareError > 0);
    }

    [Fact]
    public void ConstantYIsAPerfectHorizontalFit()
    {
        DataPoint[] points = [new(1, 5), new(2, 5), new(3, 5)];

        var result = GraphStatistics.LinearRegression(points);

        Assert.Equal(0, result.Slope, 12);
        Assert.Equal(5, result.Intercept, 12);
        Assert.Equal(1, result.RSquared, 12);
    }

    [Fact]
    public void RegressionRejectsTooFewOrConstantXValues()
    {
        Assert.Throws<ArgumentException>(
            () => GraphStatistics.LinearRegression([new DataPoint(1, 2)]));
        Assert.Throws<ArgumentException>(
            () => GraphStatistics.LinearRegression(
                [new DataPoint(1, 2), new DataPoint(1, 3)]));
    }

    [Fact]
    public void RegressionRejectsNonFiniteValues()
    {
        Assert.Throws<ArgumentException>(
            () => GraphStatistics.LinearRegression(
                [new DataPoint(1, 2), new DataPoint(2, double.NaN)]));
    }
}
