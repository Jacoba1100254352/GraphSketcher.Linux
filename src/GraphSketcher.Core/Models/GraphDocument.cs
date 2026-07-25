using System.Globalization;

namespace GraphSketcher.Core.Models;

/// <summary>
/// Portable, platform-neutral representation of a GraphSketcher document.
/// </summary>
public sealed class GraphDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSeriesCount = 256;
    public const int MaximumTotalPointCount = 250_000;
    public const int MaximumAnnotationCount = 10_000;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Title { get; set; } = "Untitled Graph";

    public string? Description { get; set; }

    public CanvasSettings Canvas { get; set; } = new();

    public AxisSettings XAxis { get; set; } = new() { Title = "X" };

    public AxisSettings YAxis { get; set; } = new() { Title = "Y" };

    public List<GraphSeries> Series { get; set; } = [];

    public List<GraphAnnotation> Annotations { get; set; } = [];

    /// <summary>
    /// Returns all model invariants that are currently violated.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion must be between 1 and {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 512)
        {
            errors.Add("title must contain between 1 and 512 characters.");
        }

        if (Description?.Length > 16_384)
        {
            errors.Add("description cannot exceed 16,384 characters.");
        }

        if (Canvas is null)
        {
            errors.Add("canvas is required.");
        }
        else
        {
            Canvas.AddValidationErrors(errors, "canvas");
        }

        if (XAxis is null)
        {
            errors.Add("xAxis is required.");
        }
        else
        {
            XAxis.AddValidationErrors(errors, "xAxis");
        }

        if (YAxis is null)
        {
            errors.Add("yAxis is required.");
        }
        else
        {
            YAxis.AddValidationErrors(errors, "yAxis");
        }

        ValidateSeries(errors);
        ValidateAnnotations(errors);
        return errors;
    }

    /// <summary>
    /// Throws when any document invariant is violated.
    /// </summary>
    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "The graph document is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $" - {error}")));
        }
    }

    private void ValidateSeries(List<string> errors)
    {
        if (Series is null)
        {
            errors.Add("series is required.");
            return;
        }

        if (Series.Count > MaximumSeriesCount)
        {
            errors.Add(
                "series cannot contain more than " +
                $"{MaximumSeriesCount.ToString("N0", CultureInfo.InvariantCulture)} items.");
            return;
        }

        long totalPointCount = 0;
        foreach (var series in Series)
        {
            if (series?.Points is null)
            {
                continue;
            }

            totalPointCount += series.Points.Count;
            if (totalPointCount > MaximumTotalPointCount)
            {
                errors.Add(
                    $"series cannot contain more than " +
                    $"{MaximumTotalPointCount.ToString("N0", CultureInfo.InvariantCulture)} " +
                    "total points.");
                return;
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Series.Count; index++)
        {
            var series = Series[index];
            if (series is null)
            {
                errors.Add($"series[{index}] is required.");
                continue;
            }

            series.AddValidationErrors(errors, $"series[{index}]");
            if (!string.IsNullOrWhiteSpace(series.Id) && !ids.Add(series.Id))
            {
                errors.Add($"series[{index}].id duplicates another series id.");
            }
        }
    }

    private void ValidateAnnotations(List<string> errors)
    {
        if (Annotations is null)
        {
            errors.Add("annotations is required.");
            return;
        }

        if (Annotations.Count > MaximumAnnotationCount)
        {
            errors.Add(
                $"annotations cannot contain more than " +
                $"{MaximumAnnotationCount.ToString("N0", CultureInfo.InvariantCulture)} items.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Annotations.Count; index++)
        {
            var annotation = Annotations[index];
            if (annotation is null)
            {
                errors.Add($"annotations[{index}] is required.");
                continue;
            }

            annotation.AddValidationErrors(errors, $"annotations[{index}]");
            if (!string.IsNullOrWhiteSpace(annotation.Id) && !ids.Add(annotation.Id))
            {
                errors.Add($"annotations[{index}].id duplicates another annotation id.");
            }
        }
    }
}
