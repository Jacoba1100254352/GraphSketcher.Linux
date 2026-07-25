namespace GraphSketcher.Core.Models;

/// <summary>
/// Represents a named collection of data points and its presentation.
/// </summary>
public sealed class GraphSeries
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Series";

    public bool IsVisible { get; set; } = true;

    public bool FillArea { get; set; }

    public LineStyle LineStyle { get; set; } = LineStyle.Solid;

    public LineMode LineMode { get; set; } = LineMode.Straight;

    public MarkerShape MarkerShape { get; set; } = MarkerShape.Circle;

    public string Color { get; set; } = "#2563EB";

    public double StrokeWidth { get; set; } = 2;

    public double MarkerSize { get; set; } = 6;

    public List<DataPoint> Points { get; set; } = [];

    internal void AddValidationErrors(List<string> errors, string path)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128)
        {
            errors.Add($"{path}.id must contain between 1 and 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 512)
        {
            errors.Add($"{path}.name must contain between 1 and 512 characters.");
        }

        if (!Enum.IsDefined(LineStyle))
        {
            errors.Add($"{path}.lineStyle is not recognized.");
        }

        if (!Enum.IsDefined(LineMode))
        {
            errors.Add($"{path}.lineMode is not recognized.");
        }

        if (!Enum.IsDefined(MarkerShape))
        {
            errors.Add($"{path}.markerShape is not recognized.");
        }

        ModelValidation.AddColorError(errors, Color, $"{path}.color");
        ModelValidation.AddNonNegativeFiniteError(errors, StrokeWidth, $"{path}.strokeWidth");
        ModelValidation.AddNonNegativeFiniteError(errors, MarkerSize, $"{path}.markerSize");

        if (Points is null)
        {
            errors.Add($"{path}.points is required.");
            return;
        }

        for (var index = 0; index < Points.Count; index++)
        {
            var point = Points[index];
            if (point is null)
            {
                errors.Add($"{path}.points[{index}] is required.");
            }
            else
            {
                point.AddValidationErrors(errors, $"{path}.points[{index}]");
            }
        }
    }
}
