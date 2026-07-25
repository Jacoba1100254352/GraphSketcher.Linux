namespace GraphSketcher.Core.Models;

/// <summary>
/// Represents one point in graph data coordinates.
/// </summary>
public sealed class DataPoint
{
    public DataPoint()
    {
    }

    public DataPoint(double x, double y, string? label = null)
    {
        X = x;
        Y = y;
        Label = label;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the symmetric horizontal error magnitude.
    /// </summary>
    public double? XError { get; set; }

    /// <summary>
    /// Gets or sets the symmetric vertical error magnitude.
    /// </summary>
    public double? YError { get; set; }

    internal void AddValidationErrors(List<string> errors, string path)
    {
        ModelValidation.AddFiniteError(errors, X, $"{path}.x");
        ModelValidation.AddFiniteError(errors, Y, $"{path}.y");

        if (XError is { } xError)
        {
            ModelValidation.AddNonNegativeFiniteError(errors, xError, $"{path}.xError");
        }

        if (YError is { } yError)
        {
            ModelValidation.AddNonNegativeFiniteError(errors, yError, $"{path}.yError");
        }

        if (Label?.Length > 2_048)
        {
            errors.Add($"{path}.label cannot exceed 2,048 characters.");
        }
    }
}
