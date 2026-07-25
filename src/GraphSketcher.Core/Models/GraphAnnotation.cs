namespace GraphSketcher.Core.Models;

/// <summary>
/// Represents text or simple geometry layered over a graph.
/// </summary>
public sealed class GraphAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public AnnotationKind Kind { get; set; } = AnnotationKind.Text;

    public AnnotationCoordinateSpace CoordinateSpace { get; set; } = AnnotationCoordinateSpace.Data;

    public double X { get; set; }

    public double Y { get; set; }

    public double? X2 { get; set; }

    public double? Y2 { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Color { get; set; } = "#111827";

    public string FillColor { get; set; } = "#00000000";

    public double StrokeWidth { get; set; } = 1.5;

    public double FontSize { get; set; } = 14;

    internal void AddValidationErrors(List<string> errors, string path)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128)
        {
            errors.Add($"{path}.id must contain between 1 and 128 characters.");
        }

        if (!Enum.IsDefined(Kind))
        {
            errors.Add($"{path}.kind is not recognized.");
        }

        if (!Enum.IsDefined(CoordinateSpace))
        {
            errors.Add($"{path}.coordinateSpace is not recognized.");
        }

        ModelValidation.AddFiniteError(errors, X, $"{path}.x");
        ModelValidation.AddFiniteError(errors, Y, $"{path}.y");

        if (X2 is { } x2)
        {
            ModelValidation.AddFiniteError(errors, x2, $"{path}.x2");
        }

        if (Y2 is { } y2)
        {
            ModelValidation.AddFiniteError(errors, y2, $"{path}.y2");
        }

        if (Kind != AnnotationKind.Text && (X2 is null || Y2 is null))
        {
            errors.Add($"{path}.x2 and {path}.y2 are required for non-text annotations.");
        }

        if (Kind == AnnotationKind.Text && string.IsNullOrWhiteSpace(Text))
        {
            errors.Add($"{path}.text is required for text annotations.");
        }

        if (Text is null || Text.Length > 8_192)
        {
            errors.Add($"{path}.text is required and cannot exceed 8,192 characters.");
        }

        ModelValidation.AddColorError(errors, Color, $"{path}.color");
        ModelValidation.AddColorError(errors, FillColor, $"{path}.fillColor");
        ModelValidation.AddNonNegativeFiniteError(errors, StrokeWidth, $"{path}.strokeWidth");
        ModelValidation.AddPositiveFiniteError(errors, FontSize, $"{path}.fontSize");
    }
}
