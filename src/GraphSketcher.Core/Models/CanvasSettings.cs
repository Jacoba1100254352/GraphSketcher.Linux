using System.Text.Json.Serialization;

namespace GraphSketcher.Core.Models;

/// <summary>
/// Describes the exported canvas and the inset occupied by the plot.
/// </summary>
public sealed class CanvasSettings
{
    public double Width { get; set; } = 960;

    public double Height { get; set; } = 640;

    public double PaddingLeft { get; set; } = 80;

    public double PaddingTop { get; set; } = 40;

    public double PaddingRight { get; set; } = 32;

    public double PaddingBottom { get; set; } = 68;

    public string BackgroundColor { get; set; } = "#FFFFFF";

    public bool ShowLegend { get; set; } = true;

    public LegendPosition LegendPosition { get; set; } = LegendPosition.TopRight;

    [JsonIgnore]
    public double PlotWidth => Width - PaddingLeft - PaddingRight;

    [JsonIgnore]
    public double PlotHeight => Height - PaddingTop - PaddingBottom;

    internal void AddValidationErrors(List<string> errors, string path)
    {
        ModelValidation.AddPositiveFiniteError(errors, Width, $"{path}.width");
        ModelValidation.AddPositiveFiniteError(errors, Height, $"{path}.height");
        ModelValidation.AddNonNegativeFiniteError(errors, PaddingLeft, $"{path}.paddingLeft");
        ModelValidation.AddNonNegativeFiniteError(errors, PaddingTop, $"{path}.paddingTop");
        ModelValidation.AddNonNegativeFiniteError(errors, PaddingRight, $"{path}.paddingRight");
        ModelValidation.AddNonNegativeFiniteError(errors, PaddingBottom, $"{path}.paddingBottom");
        ModelValidation.AddColorError(errors, BackgroundColor, $"{path}.backgroundColor");

        if (!Enum.IsDefined(LegendPosition))
        {
            errors.Add($"{path}.legendPosition is not recognized.");
        }

        if (double.IsFinite(PlotWidth) && PlotWidth <= 0)
        {
            errors.Add($"{path} horizontal padding must leave a positive plot width.");
        }

        if (double.IsFinite(PlotHeight) && PlotHeight <= 0)
        {
            errors.Add($"{path} vertical padding must leave a positive plot height.");
        }
    }
}
