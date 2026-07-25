using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

public readonly record struct AxisRange(double Minimum, double Maximum)
{
    public double Span => Maximum - Minimum;

    public bool Contains(double value) => value >= Minimum && value <= Maximum;
}

/// <summary>
/// Supplies graph-specific scaling, tick generation, and coordinate transforms.
/// </summary>
public static class GraphMath
{
    private const int MaximumGeneratedTicks = 10_000;

    public static AxisRange AutoScale(
        IEnumerable<double> values,
        AxisScale scale = AxisScale.Linear,
        int desiredTickCount = 6,
        bool includeZero = false,
        double paddingFraction = 0.05,
        double logarithmBase = 10)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateScaleArguments(scale, desiredTickCount, paddingFraction, logarithmBase);

        var usable = values
            .Where(double.IsFinite)
            .Where(value => scale != AxisScale.Logarithmic || value > 0)
            .ToArray();

        if (usable.Length == 0)
        {
            return scale == AxisScale.Logarithmic
                ? new AxisRange(1, logarithmBase)
                : new AxisRange(-1, 1);
        }

        return scale switch
        {
            AxisScale.Linear => AutoScaleLinear(
                usable.Min(),
                usable.Max(),
                desiredTickCount,
                includeZero,
                paddingFraction),
            AxisScale.Logarithmic => AutoScaleLogarithmic(
                usable.Min(),
                usable.Max(),
                paddingFraction,
                logarithmBase),
            _ => throw new ArgumentOutOfRangeException(nameof(scale)),
        };
    }

    public static AxisRange ResolveRange(
        IEnumerable<double> values,
        AxisSettings axis,
        bool includeZero = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(axis);

        var automatic = AutoScale(
            values,
            axis.Scale,
            axis.DesiredTickCount,
            includeZero,
            logarithmBase: axis.LogarithmBase);
        var minimum = axis.Minimum ?? automatic.Minimum;
        var maximum = axis.Maximum ?? automatic.Maximum;

        if (!double.IsFinite(minimum) || !double.IsFinite(maximum))
        {
            throw new ArgumentException("Axis bounds must be finite.", nameof(axis));
        }

        if (axis.Scale == AxisScale.Logarithmic && (minimum <= 0 || maximum <= 0))
        {
            throw new ArgumentException(
                "Logarithmic axis bounds must be greater than zero.",
                nameof(axis));
        }

        if (minimum >= maximum)
        {
            if (axis.Minimum is not null && axis.Maximum is not null)
            {
                throw new ArgumentException(
                    "The minimum axis bound must be less than the maximum.",
                    nameof(axis));
            }

            if (axis.Scale == AxisScale.Logarithmic)
            {
                if (axis.Minimum is not null)
                {
                    maximum = minimum * axis.LogarithmBase;
                }
                else
                {
                    minimum = maximum / axis.LogarithmBase;
                }
            }
            else
            {
                var delta = Math.Max(Math.Abs(axis.Minimum ?? maximum) * 0.1, 1);
                if (axis.Minimum is not null)
                {
                    maximum = minimum + delta;
                }
                else
                {
                    minimum = maximum - delta;
                }
            }
        }

        return new AxisRange(minimum, maximum);
    }

    public static IReadOnlyList<double> CreateTicks(
        double minimum,
        double maximum,
        AxisScale scale = AxisScale.Linear,
        int desiredTickCount = 6,
        double logarithmBase = 10,
        double? tickSpacing = null)
    {
        ValidateBounds(minimum, maximum, scale, desiredTickCount, logarithmBase);
        if (tickSpacing is < 0 || tickSpacing is { } spacing && !double.IsFinite(spacing))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tickSpacing),
                "Tick spacing must be null, zero, or a positive finite number.");
        }

        return scale switch
        {
            AxisScale.Linear => CreateLinearTicks(
                minimum,
                maximum,
                desiredTickCount,
                tickSpacing is > 0 ? tickSpacing.Value : null),
            AxisScale.Logarithmic => CreateLogarithmicTicks(
                minimum,
                maximum,
                desiredTickCount,
                logarithmBase,
                tickSpacing is > 0 ? tickSpacing.Value : null),
            _ => throw new ArgumentOutOfRangeException(nameof(scale)),
        };
    }

    public static double MapToUnit(
        double value,
        AxisRange range,
        AxisScale scale = AxisScale.Linear,
        double logarithmBase = 10)
    {
        if (!double.IsFinite(value) ||
            !double.IsFinite(range.Minimum) ||
            !double.IsFinite(range.Maximum) ||
            range.Minimum >= range.Maximum)
        {
            throw new ArgumentException("The value and axis range must be finite and ordered.");
        }

        if (scale == AxisScale.Logarithmic)
        {
            if (value <= 0 || range.Minimum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Logarithmic values and bounds must be greater than zero.");
            }

            ValidateLogarithmBase(logarithmBase);
            var minimumLog = Math.Log(range.Minimum, logarithmBase);
            return (Math.Log(value, logarithmBase) - minimumLog) /
                   (Math.Log(range.Maximum, logarithmBase) - minimumLog);
        }

        if (scale != AxisScale.Linear)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return (value - range.Minimum) / range.Span;
    }

    private static AxisRange AutoScaleLinear(
        double minimum,
        double maximum,
        int desiredTickCount,
        bool includeZero,
        double paddingFraction)
    {
        if (includeZero)
        {
            minimum = Math.Min(0, minimum);
            maximum = Math.Max(0, maximum);
        }

        if (minimum == maximum)
        {
            var delta = minimum == 0
                ? 1
                : Math.Max(Math.Abs(minimum) * Math.Max(paddingFraction, 0.1), double.Epsilon);
            minimum -= delta;
            maximum += delta;
        }
        else
        {
            var padding = (maximum - minimum) * paddingFraction;
            if (double.IsFinite(padding))
            {
                minimum -= padding;
                maximum += padding;
            }
        }

        var step = NiceNumber((maximum - minimum) / (desiredTickCount - 1), round: true);
        if (!double.IsFinite(step) || step <= 0)
        {
            return new AxisRange(minimum, maximum);
        }

        var niceMinimum = Math.Floor(minimum / step) * step;
        var niceMaximum = Math.Ceiling(maximum / step) * step;
        return double.IsFinite(niceMinimum) &&
               double.IsFinite(niceMaximum) &&
               niceMinimum < niceMaximum
            ? new AxisRange(NormalizeZero(niceMinimum), NormalizeZero(niceMaximum))
            : new AxisRange(minimum, maximum);
    }

    private static AxisRange AutoScaleLogarithmic(
        double minimum,
        double maximum,
        double paddingFraction,
        double logarithmBase)
    {
        var minimumExponent = Math.Log(minimum, logarithmBase);
        var maximumExponent = Math.Log(maximum, logarithmBase);
        if (minimum == maximum)
        {
            var halfSpan = Math.Max(0.5, paddingFraction);
            minimumExponent -= halfSpan;
            maximumExponent += halfSpan;
        }

        var lower = Math.Pow(logarithmBase, Math.Floor(minimumExponent));
        var upper = Math.Pow(logarithmBase, Math.Ceiling(maximumExponent));
        return new AxisRange(lower, upper);
    }

    private static double[] CreateLinearTicks(
        double minimum,
        double maximum,
        int desiredTickCount,
        double? requestedSpacing)
    {
        var spacing = requestedSpacing ??
                      NiceNumber((maximum - minimum) / (desiredTickCount - 1), round: true);
        if (!double.IsFinite(spacing) || spacing <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedSpacing),
                "The axis range is too small to generate finite ticks.");
        }

        var tolerance = spacing * 1e-10;
        var firstMultiplier = Math.Ceiling((minimum - tolerance) / spacing);
        var lastMultiplier = Math.Floor((maximum + tolerance) / spacing);
        var countAsDouble = lastMultiplier - firstMultiplier + 1;
        if (countAsDouble > MaximumGeneratedTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedSpacing),
                $"Tick spacing would generate more than {MaximumGeneratedTicks:N0} ticks.");
        }

        var count = Math.Max(0, (int)countAsDouble);
        var ticks = new double[count];
        for (var index = 0; index < count; index++)
        {
            ticks[index] = NormalizeZero((firstMultiplier + index) * spacing);
        }

        return ticks;
    }

    private static IReadOnlyList<double> CreateLogarithmicTicks(
        double minimum,
        double maximum,
        int desiredTickCount,
        double logarithmBase,
        double? requestedExponentSpacing)
    {
        var minimumExponent = Math.Log(minimum, logarithmBase);
        var maximumExponent = Math.Log(maximum, logarithmBase);
        var firstExponent = Math.Ceiling(minimumExponent - 1e-12);
        var lastExponent = Math.Floor(maximumExponent + 1e-12);

        var exponentSpacing = requestedExponentSpacing ??
                              Math.Max(
                                  1,
                                  Math.Ceiling(
                                      (lastExponent - firstExponent + 1) /
                                      Math.Max(1, desiredTickCount)));
        var majorTicks = new List<double>();
        for (var exponent = firstExponent;
             exponent <= lastExponent + 1e-12;
             exponent += exponentSpacing)
        {
            if (majorTicks.Count >= MaximumGeneratedTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedExponentSpacing),
                    $"Tick spacing would generate more than {MaximumGeneratedTicks:N0} ticks.");
            }

            AddIfInRange(majorTicks, Math.Pow(logarithmBase, exponent), minimum, maximum);
        }

        if (requestedExponentSpacing is null &&
            logarithmBase == 10 &&
            majorTicks.Count < desiredTickCount)
        {
            var ticks = new List<double>(majorTicks);
            for (var exponent = Math.Floor(minimumExponent);
                 exponent <= Math.Ceiling(maximumExponent);
                 exponent++)
            {
                var power = Math.Pow(10, exponent);
                AddIfInRange(ticks, 2 * power, minimum, maximum);
                AddIfInRange(ticks, 5 * power, minimum, maximum);
            }

            ticks.Sort();
            var distinctTicks = ticks.Distinct().ToArray();
            return distinctTicks.Length > 0 ? distinctTicks : [minimum, maximum];
        }

        return majorTicks.Count > 0 ? majorTicks : [minimum, maximum];
    }

    private static void AddIfInRange(
        List<double> values,
        double value,
        double minimum,
        double maximum)
    {
        if (double.IsFinite(value) &&
            value >= minimum * (1 - 1e-12) &&
            value <= maximum * (1 + 1e-12))
        {
            values.Add(value);
        }
    }

    private static double NiceNumber(double value, bool round)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return value;
        }

        var exponent = Math.Floor(Math.Log10(value));
        var fraction = value / Math.Pow(10, exponent);
        double niceFraction;

        if (round)
        {
            niceFraction = fraction switch
            {
                < 1.5 => 1,
                < 3 => 2,
                < 7 => 5,
                _ => 10,
            };
        }
        else
        {
            niceFraction = fraction switch
            {
                <= 1 => 1,
                <= 2 => 2,
                <= 5 => 5,
                _ => 10,
            };
        }

        return niceFraction * Math.Pow(10, exponent);
    }

    private static double NormalizeZero(double value) => value == 0 ? 0 : value;

    private static void ValidateScaleArguments(
        AxisScale scale,
        int desiredTickCount,
        double paddingFraction,
        double logarithmBase)
    {
        if (!Enum.IsDefined(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        if (desiredTickCount is < 2 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredTickCount),
                "The desired tick count must be between 2 and 100.");
        }

        if (!double.IsFinite(paddingFraction) || paddingFraction is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paddingFraction),
                "The padding fraction must be between zero and one.");
        }

        if (scale == AxisScale.Logarithmic)
        {
            ValidateLogarithmBase(logarithmBase);
        }
    }

    private static void ValidateBounds(
        double minimum,
        double maximum,
        AxisScale scale,
        int desiredTickCount,
        double logarithmBase)
    {
        ValidateScaleArguments(scale, desiredTickCount, 0, logarithmBase);
        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            minimum >= maximum)
        {
            throw new ArgumentException("Axis bounds must be finite and minimum must be less than maximum.");
        }

        if (scale == AxisScale.Logarithmic && minimum <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "Logarithmic axis bounds must be greater than zero.");
        }
    }

    private static void ValidateLogarithmBase(double logarithmBase)
    {
        if (!double.IsFinite(logarithmBase) || logarithmBase <= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logarithmBase),
                "The logarithm base must be finite and greater than one.");
        }
    }
}
