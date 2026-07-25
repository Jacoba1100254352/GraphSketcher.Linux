using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class GraphMathTests
{
    [Fact]
    public void LinearAutoScaleProducesNiceBoundsContainingData()
    {
        var range = GraphMath.AutoScale([1, 9], desiredTickCount: 6);

        Assert.Equal(0, range.Minimum);
        Assert.Equal(10, range.Maximum);
        Assert.True(range.Contains(1));
        Assert.True(range.Contains(9));
    }

    [Fact]
    public void LinearAutoScaleHandlesEmptyAndConstantData()
    {
        var empty = GraphMath.AutoScale([]);
        var constant = GraphMath.AutoScale([5, 5]);

        Assert.Equal(new AxisRange(-1, 1), empty);
        Assert.True(constant.Minimum < 5);
        Assert.True(constant.Maximum > 5);
    }

    [Fact]
    public void IncludeZeroExtendsPositiveRange()
    {
        var range = GraphMath.AutoScale([10, 20], includeZero: true);

        Assert.True(range.Minimum <= 0);
        Assert.True(range.Maximum >= 20);
    }

    [Fact]
    public void ExplicitLinearTickSpacingIsHonored()
    {
        var ticks = GraphMath.CreateTicks(0, 10, tickSpacing: 2.5);

        Assert.Equal([0d, 2.5, 5, 7.5, 10], ticks);
    }

    [Fact]
    public void AutomaticLinearTicksUseOneTwoFiveProgression()
    {
        var ticks = GraphMath.CreateTicks(0, 10, desiredTickCount: 6);

        Assert.Equal([0d, 2, 4, 6, 8, 10], ticks);
    }

    [Fact]
    public void LogAutoScaleIgnoresNonPositiveValuesAndUsesPositiveBounds()
    {
        var range = GraphMath.AutoScale(
            [-10, 0, 2, 800],
            AxisScale.Logarithmic);

        Assert.Equal(1, range.Minimum);
        Assert.Equal(1000, range.Maximum);
        Assert.True(range.Contains(2));
        Assert.True(range.Contains(800));
    }

    [Fact]
    public void LogTicksContainDecadesAndUsefulMinorTicks()
    {
        var ticks = GraphMath.CreateTicks(
            1,
            1000,
            AxisScale.Logarithmic,
            desiredTickCount: 8);

        Assert.Contains(1, ticks);
        Assert.Contains(10, ticks);
        Assert.Contains(100, ticks);
        Assert.Contains(1000, ticks);
        Assert.Contains(2, ticks);
        Assert.Contains(5, ticks);
    }

    [Fact]
    public void NarrowLogRangeStillProducesTicks()
    {
        var ticks = GraphMath.CreateTicks(
            3,
            4,
            AxisScale.Logarithmic);

        Assert.Equal([3d, 4d], ticks);
    }

    [Fact]
    public void ResolveRangeHonorsManualBoundAndRepairsAutomaticBound()
    {
        var axis = new AxisSettings { Minimum = 100 };

        var range = GraphMath.ResolveRange([1, 2, 3], axis);

        Assert.Equal(100, range.Minimum);
        Assert.True(range.Maximum > 100);
    }

    [Fact]
    public void UnitMappingSupportsLinearAndLogarithmicAxes()
    {
        var linear = GraphMath.MapToUnit(5, new AxisRange(0, 10));
        var logarithmic = GraphMath.MapToUnit(
            10,
            new AxisRange(1, 100),
            AxisScale.Logarithmic);

        Assert.Equal(0.5, linear, 12);
        Assert.Equal(0.5, logarithmic, 12);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 1)]
    public void CreateTicksRejectsUnorderedBounds(double minimum, double maximum)
    {
        Assert.Throws<ArgumentException>(
            () => GraphMath.CreateTicks(minimum, maximum));
    }
}
