using System.Globalization;

namespace GraphSketcher.Core.Models;

/// <summary>
/// Describes one graph axis. Null bounds request automatic scaling.
/// </summary>
public sealed class AxisSettings
{
    public string Title { get; set; } = string.Empty;

    public AxisScale Scale { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public bool IsReversed { get; set; }

    public bool ShowGridLines { get; set; } = true;

    public bool ShowAxisLine { get; set; } = true;

    public bool ShowTickLabels { get; set; } = true;

    public int DesiredTickCount { get; set; } = 6;

    /// <summary>
    /// Gets or sets an explicit tick interval. For logarithmic axes this is an exponent interval.
    /// Null or zero requests automatic spacing.
    /// </summary>
    public double? TickSpacing { get; set; }

    public string NumberFormat { get; set; } = "G4";

    public double LogarithmBase { get; set; } = 10;

    internal void AddValidationErrors(List<string> errors, string path)
    {
        if (!Enum.IsDefined(Scale))
        {
            errors.Add($"{path}.scale is not recognized.");
        }

        if (Title is null || Title.Length > 512)
        {
            errors.Add($"{path}.title is required and cannot exceed 512 characters.");
        }

        if (Minimum is { } minimum)
        {
            ModelValidation.AddFiniteError(errors, minimum, $"{path}.minimum");
        }

        if (Maximum is { } maximum)
        {
            ModelValidation.AddFiniteError(errors, maximum, $"{path}.maximum");
        }

        if (Minimum is { } lower && Maximum is { } upper &&
            double.IsFinite(lower) && double.IsFinite(upper) &&
            lower >= upper)
        {
            errors.Add($"{path}.minimum must be less than {path}.maximum.");
        }

        if (Scale == AxisScale.Logarithmic)
        {
            if (Minimum is <= 0)
            {
                errors.Add($"{path}.minimum must be greater than zero for a logarithmic axis.");
            }

            if (Maximum is <= 0)
            {
                errors.Add($"{path}.maximum must be greater than zero for a logarithmic axis.");
            }

            if (!double.IsFinite(LogarithmBase) || LogarithmBase <= 1)
            {
                errors.Add($"{path}.logarithmBase must be finite and greater than one.");
            }
        }

        if (DesiredTickCount is < 2 or > 100)
        {
            errors.Add($"{path}.desiredTickCount must be between 2 and 100.");
        }

        if (TickSpacing is { } tickSpacing &&
            (!double.IsFinite(tickSpacing) || tickSpacing < 0))
        {
            errors.Add($"{path}.tickSpacing must be null, zero, or a positive finite number.");
        }

        if (string.IsNullOrWhiteSpace(NumberFormat) || NumberFormat.Length > 32)
        {
            errors.Add($"{path}.numberFormat must contain between 1 and 32 characters.");
        }
        else
        {
            try
            {
                _ = 0d.ToString(NumberFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                errors.Add($"{path}.numberFormat is not a valid .NET numeric format.");
            }
        }
    }
}
