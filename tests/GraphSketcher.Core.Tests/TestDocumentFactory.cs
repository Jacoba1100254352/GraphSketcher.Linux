using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Tests;

internal static class TestDocumentFactory
{
    public static GraphDocument Create()
    {
        var document = new GraphDocument
        {
            Title = "Quarterly results",
            Description = "Portable document test",
            Canvas = new CanvasSettings
            {
                Width = 800,
                Height = 500,
                PaddingLeft = 70,
                PaddingTop = 45,
                PaddingRight = 30,
                PaddingBottom = 60,
                BackgroundColor = "#FAFAFA",
                ShowLegend = true,
                LegendPosition = LegendPosition.BottomLeft,
            },
            XAxis = new AxisSettings
            {
                Title = "Quarter",
                Minimum = 0,
                Maximum = 4,
                DesiredTickCount = 5,
                TickSpacing = 1,
            },
            YAxis = new AxisSettings
            {
                Title = "Revenue",
                Minimum = 0,
                Maximum = 10,
                NumberFormat = "0.0",
            },
        };

        document.Series.Add(new GraphSeries
        {
            Id = "revenue",
            Name = "Revenue",
            Color = "#2563EB",
            FillArea = true,
            LineStyle = LineStyle.Dashed,
            LineMode = LineMode.Smooth,
            MarkerShape = MarkerShape.Diamond,
            Points =
            [
                new DataPoint(1, 2, "Q1") { XError = 0.1, YError = 0.2 },
                new DataPoint(2, 5, "Q2"),
                new DataPoint(3, 8, "Q3"),
            ],
        });
        document.Annotations.Add(new GraphAnnotation
        {
            Id = "target",
            Kind = AnnotationKind.Line,
            X = 0,
            Y = 7,
            X2 = 4,
            Y2 = 7,
            Color = "#DC2626",
        });
        return document;
    }
}
