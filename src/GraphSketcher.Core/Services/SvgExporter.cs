using System.Globalization;
using System.Text;
using System.Xml;
using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

public sealed class SvgExportOptions
{
    public bool IncludeTitle { get; set; } = true;

    public bool IncludeDescription { get; set; } = true;

    public string FontFamily { get; set; } = "Segoe UI, Arial, sans-serif";
}

/// <summary>
/// Exports graph documents as standalone, resolution-independent SVG.
/// </summary>
public static class SvgExporter
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    public static string Export(
        GraphDocument document,
        SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureValid();
        options ??= new SvgExportOptions();
        ValidateOptions(options);

        var visibleSeries = document.Series.Where(series => series.IsVisible).ToArray();
        var xValues = GetAxisValues(visibleSeries, useX: true);
        var yValues = GetAxisValues(visibleSeries, useX: false);
        var xRange = GraphMath.ResolveRange(xValues, document.XAxis);
        var yRange = GraphMath.ResolveRange(yValues, document.YAxis);
        var xTicks = GraphMath.CreateTicks(
            xRange.Minimum,
            xRange.Maximum,
            document.XAxis.Scale,
            document.XAxis.DesiredTickCount,
            document.XAxis.LogarithmBase,
            document.XAxis.TickSpacing);
        var yTicks = GraphMath.CreateTicks(
            yRange.Minimum,
            yRange.Maximum,
            document.YAxis.Scale,
            document.YAxis.DesiredTickCount,
            document.YAxis.LogarithmBase,
            document.YAxis.TickSpacing);

        var buffer = new StringBuilder();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.Entitize,
        }))
        {
            WriteDocument(
                writer,
                document,
                options,
                visibleSeries,
                xRange,
                yRange,
                xTicks,
                yTicks);
        }

        return buffer.ToString();
    }

    public static async Task ExportAsync(
        GraphDocument document,
        Stream destination,
        SvgExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var svg = Export(document, options);
        var bytes = Encoding.UTF8.GetBytes(svg);
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ExportAsync(
        GraphDocument document,
        string path,
        SvgExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ExportAsync(document, stream, options, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteDocument(
        XmlWriter writer,
        GraphDocument document,
        SvgExportOptions options,
        GraphSeries[] visibleSeries,
        AxisRange xRange,
        AxisRange yRange,
        IReadOnlyList<double> xTicks,
        IReadOnlyList<double> yTicks)
    {
        var canvas = document.Canvas;
        writer.WriteStartElement("svg", SvgNamespace);
        writer.WriteAttributeString("width", Format(canvas.Width));
        writer.WriteAttributeString("height", Format(canvas.Height));
        writer.WriteAttributeString(
            "viewBox",
            $"0 0 {Format(canvas.Width)} {Format(canvas.Height)}");
        writer.WriteAttributeString("role", "img");
        writer.WriteAttributeString("aria-labelledby", "graph-title graph-description");

        writer.WriteStartElement("title", SvgNamespace);
        writer.WriteAttributeString("id", "graph-title");
        writer.WriteString(document.Title);
        writer.WriteEndElement();
        writer.WriteStartElement("desc", SvgNamespace);
        writer.WriteAttributeString("id", "graph-description");
        writer.WriteString(
            options.IncludeDescription && !string.IsNullOrWhiteSpace(document.Description)
                ? document.Description
                : $"Graph titled {document.Title}");
        writer.WriteEndElement();

        WriteDefinitions(writer, canvas);
        WriteRectangle(
            writer,
            x: 0,
            y: 0,
            canvas.Width,
            canvas.Height,
            canvas.BackgroundColor);

        if (options.IncludeTitle)
        {
            WriteText(
                writer,
                canvas.Width / 2,
                Math.Max(22, canvas.PaddingTop / 2),
                document.Title,
                options.FontFamily,
                fontSize: 18,
                anchor: "middle",
                weight: "600");
        }

        WriteGridAndAxes(
            writer,
            document,
            options,
            xRange,
            yRange,
            xTicks,
            yTicks);

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("clip-path", "url(#plot-clip)");
        foreach (var series in visibleSeries)
        {
            WriteSeries(writer, series, document, xRange, yRange, options.FontFamily);
        }

        foreach (var annotation in document.Annotations)
        {
            WriteAnnotation(writer, annotation, document, xRange, yRange, options.FontFamily);
        }

        writer.WriteEndElement();

        if (canvas.ShowLegend && visibleSeries.Length > 0)
        {
            WriteLegend(writer, visibleSeries, canvas, options.FontFamily);
        }

        writer.WriteEndElement();
    }

    private static void WriteDefinitions(XmlWriter writer, CanvasSettings canvas)
    {
        writer.WriteStartElement("defs", SvgNamespace);
        writer.WriteStartElement("clipPath", SvgNamespace);
        writer.WriteAttributeString("id", "plot-clip");
        writer.WriteStartElement("rect", SvgNamespace);
        WriteGeometryAttributes(
            writer,
            canvas.PaddingLeft,
            canvas.PaddingTop,
            canvas.PlotWidth,
            canvas.PlotHeight);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteGridAndAxes(
        XmlWriter writer,
        GraphDocument document,
        SvgExportOptions options,
        AxisRange xRange,
        AxisRange yRange,
        IReadOnlyList<double> xTicks,
        IReadOnlyList<double> yTicks)
    {
        var canvas = document.Canvas;
        var left = canvas.PaddingLeft;
        var top = canvas.PaddingTop;
        var right = left + canvas.PlotWidth;
        var bottom = top + canvas.PlotHeight;

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "grid");
        writer.WriteAttributeString("stroke", "#D1D5DB");
        writer.WriteAttributeString("stroke-width", "1");

        if (document.XAxis.ShowGridLines)
        {
            foreach (var tick in xTicks)
            {
                var x = MapX(tick, document.XAxis, xRange, canvas);
                WriteLine(writer, x, top, x, bottom);
            }
        }

        if (document.YAxis.ShowGridLines)
        {
            foreach (var tick in yTicks)
            {
                var y = MapY(tick, document.YAxis, yRange, canvas);
                WriteLine(writer, left, y, right, y);
            }
        }

        writer.WriteEndElement();

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "axes");
        writer.WriteAttributeString("stroke", "#374151");
        writer.WriteAttributeString("fill", "#111827");

        if (document.XAxis.ShowAxisLine)
        {
            WriteLine(writer, left, bottom, right, bottom);
        }

        if (document.YAxis.ShowAxisLine)
        {
            WriteLine(writer, left, top, left, bottom);
        }

        foreach (var tick in xTicks)
        {
            var x = MapX(tick, document.XAxis, xRange, canvas);
            if (document.XAxis.ShowAxisLine)
            {
                WriteLine(writer, x, bottom, x, bottom + 5);
            }

            if (document.XAxis.ShowTickLabels)
            {
                WriteText(
                    writer,
                    x,
                    bottom + 20,
                    FormatTick(tick, document.XAxis.NumberFormat),
                    options.FontFamily,
                    12,
                    "middle");
            }
        }

        foreach (var tick in yTicks)
        {
            var y = MapY(tick, document.YAxis, yRange, canvas);
            if (document.YAxis.ShowAxisLine)
            {
                WriteLine(writer, left - 5, y, left, y);
            }

            if (document.YAxis.ShowTickLabels)
            {
                WriteText(
                    writer,
                    left - 10,
                    y + 4,
                    FormatTick(tick, document.YAxis.NumberFormat),
                    options.FontFamily,
                    12,
                    "end");
            }
        }

        writer.WriteEndElement();

        if (!string.IsNullOrWhiteSpace(document.XAxis.Title))
        {
            WriteText(
                writer,
                left + (canvas.PlotWidth / 2),
                canvas.Height - 14,
                document.XAxis.Title,
                options.FontFamily,
                14,
                "middle",
                "600");
        }

        if (!string.IsNullOrWhiteSpace(document.YAxis.Title))
        {
            writer.WriteStartElement("text", SvgNamespace);
            writer.WriteAttributeString(
                "transform",
                $"translate(18 {Format(top + (canvas.PlotHeight / 2))}) rotate(-90)");
            writer.WriteAttributeString("font-family", options.FontFamily);
            writer.WriteAttributeString("font-size", "14");
            writer.WriteAttributeString("font-weight", "600");
            writer.WriteAttributeString("text-anchor", "middle");
            writer.WriteString(document.YAxis.Title);
            writer.WriteEndElement();
        }
    }

    private static void WriteSeries(
        XmlWriter writer,
        GraphSeries series,
        GraphDocument document,
        AxisRange xRange,
        AxisRange yRange,
        string fontFamily)
    {
        var segments = MapSegments(series.Points, document, xRange, yRange);
        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "series");
        writer.WriteAttributeString("data-series-id", series.Id);
        writer.WriteAttributeString("data-series-name", series.Name);

        foreach (var segment in segments)
        {
            if (segment.Count > 1 &&
                series.FillArea &&
                series.LineMode != LineMode.None)
            {
                WriteArea(writer, segment, series, document, yRange);
            }

            if (segment.Count > 1 &&
                series.LineStyle != LineStyle.None &&
                series.LineMode != LineMode.None)
            {
                writer.WriteStartElement("path", SvgNamespace);
                writer.WriteAttributeString("d", BuildPath(segment, series.LineMode));
                writer.WriteAttributeString("fill", "none");
                writer.WriteAttributeString("stroke", series.Color);
                writer.WriteAttributeString("stroke-width", Format(series.StrokeWidth));
                writer.WriteAttributeString("stroke-linecap", "round");
                writer.WriteAttributeString("stroke-linejoin", "round");
                var dashArray = GetDashArray(series.LineStyle);
                if (dashArray is not null)
                {
                    writer.WriteAttributeString("stroke-dasharray", dashArray);
                }

                writer.WriteEndElement();
            }

            foreach (var point in segment)
            {
                WriteErrorBars(writer, point, series, document, xRange, yRange);
                WriteMarker(writer, point.ScreenX, point.ScreenY, series);
                if (!string.IsNullOrWhiteSpace(point.Source.Label))
                {
                    WriteText(
                        writer,
                        point.ScreenX + (series.MarkerSize / 2) + 4,
                        point.ScreenY - 4,
                        point.Source.Label,
                        fontFamily,
                        11,
                        "start");
                }
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteArea(
        XmlWriter writer,
        IReadOnlyList<MappedPoint> segment,
        GraphSeries series,
        GraphDocument document,
        AxisRange yRange)
    {
        var baselineValue = document.YAxis.Scale == AxisScale.Linear && yRange.Contains(0)
            ? 0
            : yRange.Minimum;
        var baseline = MapY(baselineValue, document.YAxis, yRange, document.Canvas);
        var path = new StringBuilder(BuildPath(segment, series.LineMode));
        path.Append(" L ")
            .Append(Format(segment[^1].ScreenX))
            .Append(' ')
            .Append(Format(baseline))
            .Append(" L ")
            .Append(Format(segment[0].ScreenX))
            .Append(' ')
            .Append(Format(baseline))
            .Append(" Z");

        writer.WriteStartElement("path", SvgNamespace);
        writer.WriteAttributeString("d", path.ToString());
        writer.WriteAttributeString("fill", series.Color);
        writer.WriteAttributeString("fill-opacity", "0.16");
        writer.WriteAttributeString("stroke", "none");
        writer.WriteEndElement();
    }

    private static void WriteErrorBars(
        XmlWriter writer,
        MappedPoint point,
        GraphSeries series,
        GraphDocument document,
        AxisRange xRange,
        AxisRange yRange)
    {
        const double cap = 4;
        if (point.Source.XError is not > 0 && point.Source.YError is not > 0)
        {
            return;
        }

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "error-bars");
        writer.WriteAttributeString("stroke", series.Color);
        writer.WriteAttributeString("stroke-width", "1");

        if (point.Source.XError is > 0)
        {
            var lower = point.Source.X - point.Source.XError.Value;
            var upper = point.Source.X + point.Source.XError.Value;
            if (CanMap(lower, document.XAxis) && CanMap(upper, document.XAxis))
            {
                var x1 = MapX(lower, document.XAxis, xRange, document.Canvas);
                var x2 = MapX(upper, document.XAxis, xRange, document.Canvas);
                WriteLine(writer, x1, point.ScreenY, x2, point.ScreenY);
                WriteLine(writer, x1, point.ScreenY - cap, x1, point.ScreenY + cap);
                WriteLine(writer, x2, point.ScreenY - cap, x2, point.ScreenY + cap);
            }
        }

        if (point.Source.YError is > 0)
        {
            var lower = point.Source.Y - point.Source.YError.Value;
            var upper = point.Source.Y + point.Source.YError.Value;
            if (CanMap(lower, document.YAxis) && CanMap(upper, document.YAxis))
            {
                var y1 = MapY(lower, document.YAxis, yRange, document.Canvas);
                var y2 = MapY(upper, document.YAxis, yRange, document.Canvas);
                WriteLine(writer, point.ScreenX, y1, point.ScreenX, y2);
                WriteLine(writer, point.ScreenX - cap, y1, point.ScreenX + cap, y1);
                WriteLine(writer, point.ScreenX - cap, y2, point.ScreenX + cap, y2);
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteMarker(
        XmlWriter writer,
        double x,
        double y,
        GraphSeries series)
    {
        if (series.MarkerShape == MarkerShape.None || series.MarkerSize <= 0)
        {
            return;
        }

        var radius = series.MarkerSize / 2;
        switch (series.MarkerShape)
        {
            case MarkerShape.Circle:
                writer.WriteStartElement("circle", SvgNamespace);
                writer.WriteAttributeString("cx", Format(x));
                writer.WriteAttributeString("cy", Format(y));
                writer.WriteAttributeString("r", Format(radius));
                WriteMarkerPaint(writer, series.Color);
                writer.WriteEndElement();
                break;
            case MarkerShape.Square:
                WriteRectangle(
                    writer,
                    x - radius,
                    y - radius,
                    series.MarkerSize,
                    series.MarkerSize,
                    series.Color,
                    series.Color);
                break;
            case MarkerShape.Triangle:
                WritePolygon(
                    writer,
                    [
                        (x, y - radius),
                        (x + radius, y + radius),
                        (x - radius, y + radius),
                    ],
                    series.Color);
                break;
            case MarkerShape.Diamond:
                WritePolygon(
                    writer,
                    [
                        (x, y - radius),
                        (x + radius, y),
                        (x, y + radius),
                        (x - radius, y),
                    ],
                    series.Color);
                break;
            case MarkerShape.Cross:
                WriteCross(writer, x, y, radius, series.Color, diagonal: true);
                break;
            case MarkerShape.Plus:
                WriteCross(writer, x, y, radius, series.Color, diagonal: false);
                break;
            case MarkerShape.None:
                break;
            default:
                throw new InvalidOperationException("The marker shape is not supported.");
        }
    }

    private static void WriteAnnotation(
        XmlWriter writer,
        GraphAnnotation annotation,
        GraphDocument document,
        AxisRange xRange,
        AxisRange yRange,
        string fontFamily)
    {
        if (!TryMapAnnotationPoint(
                annotation.X,
                annotation.Y,
                annotation.CoordinateSpace,
                document,
                xRange,
                yRange,
                out var start))
        {
            return;
        }

        MappedCoordinate? end = null;
        if (annotation.X2 is { } x2 &&
            annotation.Y2 is { } y2 &&
            TryMapAnnotationPoint(
                x2,
                y2,
                annotation.CoordinateSpace,
                document,
                xRange,
                yRange,
                out var mappedEnd))
        {
            end = mappedEnd;
        }

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "annotation");
        writer.WriteAttributeString("data-annotation-id", annotation.Id);

        switch (annotation.Kind)
        {
            case AnnotationKind.Text:
                WriteText(
                    writer,
                    start.X,
                    start.Y,
                    annotation.Text,
                    fontFamily,
                    annotation.FontSize,
                    "start",
                    color: annotation.Color);
                break;
            case AnnotationKind.Line when end is { } lineEnd:
                WriteStyledLine(writer, start, lineEnd, annotation);
                break;
            case AnnotationKind.Arrow when end is { } arrowEnd:
                WriteStyledLine(writer, start, arrowEnd, annotation);
                WriteArrowHead(writer, start, arrowEnd, annotation.Color);
                break;
            case AnnotationKind.Rectangle when end is { } rectangleEnd:
                WriteRectangle(
                    writer,
                    Math.Min(start.X, rectangleEnd.X),
                    Math.Min(start.Y, rectangleEnd.Y),
                    Math.Abs(rectangleEnd.X - start.X),
                    Math.Abs(rectangleEnd.Y - start.Y),
                    annotation.FillColor,
                    annotation.Color,
                    annotation.StrokeWidth);
                break;
            case AnnotationKind.Ellipse when end is { } ellipseEnd:
                writer.WriteStartElement("ellipse", SvgNamespace);
                writer.WriteAttributeString("cx", Format((start.X + ellipseEnd.X) / 2));
                writer.WriteAttributeString("cy", Format((start.Y + ellipseEnd.Y) / 2));
                writer.WriteAttributeString("rx", Format(Math.Abs(ellipseEnd.X - start.X) / 2));
                writer.WriteAttributeString("ry", Format(Math.Abs(ellipseEnd.Y - start.Y) / 2));
                writer.WriteAttributeString("fill", annotation.FillColor);
                writer.WriteAttributeString("stroke", annotation.Color);
                writer.WriteAttributeString("stroke-width", Format(annotation.StrokeWidth));
                writer.WriteEndElement();
                break;
        }

        writer.WriteEndElement();
    }

    private static void WriteLegend(
        XmlWriter writer,
        GraphSeries[] series,
        CanvasSettings canvas,
        string fontFamily)
    {
        const double rowHeight = 22;
        const double inset = 10;
        var longestName = series.Max(item => item.Name.Length);
        var availableWidth = Math.Max(1, canvas.PlotWidth - (inset * 2));
        var width = Math.Min(availableWidth, Math.Max(120, (longestName * 7) + 48));
        var height = (series.Length * rowHeight) + 12;
        var left = canvas.LegendPosition is LegendPosition.TopLeft or LegendPosition.BottomLeft
            ? canvas.PaddingLeft + inset
            : canvas.PaddingLeft + canvas.PlotWidth - width - inset;
        var top = canvas.LegendPosition is LegendPosition.TopLeft or LegendPosition.TopRight
            ? canvas.PaddingTop + inset
            : canvas.PaddingTop + canvas.PlotHeight - height - inset;

        writer.WriteStartElement("g", SvgNamespace);
        writer.WriteAttributeString("class", "legend");
        WriteRectangle(writer, left, top, width, height, "#FFFFFFE6", "#9CA3AF");

        for (var index = 0; index < series.Length; index++)
        {
            var item = series[index];
            var centerY = top + 17 + (index * rowHeight);
            WriteLine(
                writer,
                left + 10,
                centerY - 4,
                left + 32,
                centerY - 4,
                item.Color,
                Math.Max(1, item.StrokeWidth));
            WriteText(
                writer,
                left + 40,
                centerY,
                item.Name,
                fontFamily,
                12,
                "start");
        }

        writer.WriteEndElement();
    }

    private static List<List<MappedPoint>> MapSegments(
        IReadOnlyList<DataPoint> points,
        GraphDocument document,
        AxisRange xRange,
        AxisRange yRange)
    {
        var segments = new List<List<MappedPoint>>();
        var current = new List<MappedPoint>();
        foreach (var point in points)
        {
            if (!CanMap(point.X, document.XAxis) || !CanMap(point.Y, document.YAxis))
            {
                if (current.Count > 0)
                {
                    segments.Add(current);
                    current = [];
                }

                continue;
            }

            current.Add(new MappedPoint(
                point,
                MapX(point.X, document.XAxis, xRange, document.Canvas),
                MapY(point.Y, document.YAxis, yRange, document.Canvas)));
        }

        if (current.Count > 0)
        {
            segments.Add(current);
        }

        return segments;
    }

    private static string BuildPath(
        IReadOnlyList<MappedPoint> points,
        LineMode lineMode)
    {
        var path = new StringBuilder()
            .Append("M ")
            .Append(Format(points[0].ScreenX))
            .Append(' ')
            .Append(Format(points[0].ScreenY));

        switch (lineMode)
        {
            case LineMode.Straight:
                for (var index = 1; index < points.Count; index++)
                {
                    path.Append(" L ")
                        .Append(Format(points[index].ScreenX))
                        .Append(' ')
                        .Append(Format(points[index].ScreenY));
                }

                break;
            case LineMode.Step:
                for (var index = 1; index < points.Count; index++)
                {
                    path.Append(" H ")
                        .Append(Format(points[index].ScreenX))
                        .Append(" V ")
                        .Append(Format(points[index].ScreenY));
                }

                break;
            case LineMode.Smooth:
                for (var index = 0; index < points.Count - 1; index++)
                {
                    var before = points[Math.Max(0, index - 1)];
                    var current = points[index];
                    var next = points[index + 1];
                    var after = points[Math.Min(points.Count - 1, index + 2)];
                    var control1X = current.ScreenX + ((next.ScreenX - before.ScreenX) / 6);
                    var control1Y = current.ScreenY + ((next.ScreenY - before.ScreenY) / 6);
                    var control2X = next.ScreenX - ((after.ScreenX - current.ScreenX) / 6);
                    var control2Y = next.ScreenY - ((after.ScreenY - current.ScreenY) / 6);
                    path.Append(" C ")
                        .Append(Format(control1X)).Append(' ')
                        .Append(Format(control1Y)).Append(' ')
                        .Append(Format(control2X)).Append(' ')
                        .Append(Format(control2Y)).Append(' ')
                        .Append(Format(next.ScreenX)).Append(' ')
                        .Append(Format(next.ScreenY));
                }

                break;
            case LineMode.None:
                break;
            default:
                throw new InvalidOperationException("The line mode is not supported.");
        }

        return path.ToString();
    }

    private static double[] GetAxisValues(
        IEnumerable<GraphSeries> series,
        bool useX)
    {
        var values = new List<double>();
        foreach (var point in series.SelectMany(item => item.Points))
        {
            var value = useX ? point.X : point.Y;
            values.Add(value);
            var error = useX ? point.XError : point.YError;
            if (error is > 0)
            {
                values.Add(value - error.Value);
                values.Add(value + error.Value);
            }
        }

        return values.ToArray();
    }

    private static bool TryMapAnnotationPoint(
        double x,
        double y,
        AnnotationCoordinateSpace coordinateSpace,
        GraphDocument document,
        AxisRange xRange,
        AxisRange yRange,
        out MappedCoordinate coordinate)
    {
        if (coordinateSpace == AnnotationCoordinateSpace.Canvas)
        {
            coordinate = new MappedCoordinate(x, y);
            return true;
        }

        if (!CanMap(x, document.XAxis) || !CanMap(y, document.YAxis))
        {
            coordinate = default;
            return false;
        }

        coordinate = new MappedCoordinate(
            MapX(x, document.XAxis, xRange, document.Canvas),
            MapY(y, document.YAxis, yRange, document.Canvas));
        return true;
    }

    private static bool CanMap(double value, AxisSettings axis) =>
        double.IsFinite(value) &&
        (axis.Scale != AxisScale.Logarithmic || value > 0);

    private static double MapX(
        double value,
        AxisSettings axis,
        AxisRange range,
        CanvasSettings canvas)
    {
        var unit = GraphMath.MapToUnit(value, range, axis.Scale, axis.LogarithmBase);
        if (axis.IsReversed)
        {
            unit = 1 - unit;
        }

        return canvas.PaddingLeft + (unit * canvas.PlotWidth);
    }

    private static double MapY(
        double value,
        AxisSettings axis,
        AxisRange range,
        CanvasSettings canvas)
    {
        var unit = GraphMath.MapToUnit(value, range, axis.Scale, axis.LogarithmBase);
        if (axis.IsReversed)
        {
            unit = 1 - unit;
        }

        return canvas.PaddingTop + ((1 - unit) * canvas.PlotHeight);
    }

    private static void WriteStyledLine(
        XmlWriter writer,
        MappedCoordinate start,
        MappedCoordinate end,
        GraphAnnotation annotation) =>
        WriteLine(
            writer,
            start.X,
            start.Y,
            end.X,
            end.Y,
            annotation.Color,
            annotation.StrokeWidth);

    private static void WriteArrowHead(
        XmlWriter writer,
        MappedCoordinate start,
        MappedCoordinate end,
        string color)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double length = 10;
        const double halfWidth = 5;
        var baseX = end.X - (Math.Cos(angle) * length);
        var baseY = end.Y - (Math.Sin(angle) * length);
        var perpendicularX = -Math.Sin(angle) * halfWidth;
        var perpendicularY = Math.Cos(angle) * halfWidth;
        WritePolygon(
            writer,
            [
                (end.X, end.Y),
                (baseX + perpendicularX, baseY + perpendicularY),
                (baseX - perpendicularX, baseY - perpendicularY),
            ],
            color);
    }

    private static void WriteCross(
        XmlWriter writer,
        double x,
        double y,
        double radius,
        string color,
        bool diagonal)
    {
        if (diagonal)
        {
            WriteLine(writer, x - radius, y - radius, x + radius, y + radius, color, 1.5);
            WriteLine(writer, x - radius, y + radius, x + radius, y - radius, color, 1.5);
        }
        else
        {
            WriteLine(writer, x - radius, y, x + radius, y, color, 1.5);
            WriteLine(writer, x, y - radius, x, y + radius, color, 1.5);
        }
    }

    private static void WriteMarkerPaint(XmlWriter writer, string color)
    {
        writer.WriteAttributeString("fill", color);
        writer.WriteAttributeString("stroke", color);
    }

    private static void WritePolygon(
        XmlWriter writer,
        IReadOnlyList<(double X, double Y)> points,
        string color)
    {
        writer.WriteStartElement("polygon", SvgNamespace);
        writer.WriteAttributeString(
            "points",
            string.Join(" ", points.Select(point => $"{Format(point.X)},{Format(point.Y)}")));
        writer.WriteAttributeString("fill", color);
        writer.WriteAttributeString("stroke", color);
        writer.WriteEndElement();
    }

    private static void WriteRectangle(
        XmlWriter writer,
        double x,
        double y,
        double width,
        double height,
        string fill,
        string? stroke = null,
        double strokeWidth = 1)
    {
        writer.WriteStartElement("rect", SvgNamespace);
        WriteGeometryAttributes(writer, x, y, width, height);
        writer.WriteAttributeString("fill", fill);
        if (stroke is not null)
        {
            writer.WriteAttributeString("stroke", stroke);
            writer.WriteAttributeString("stroke-width", Format(strokeWidth));
        }

        writer.WriteEndElement();
    }

    private static void WriteGeometryAttributes(
        XmlWriter writer,
        double x,
        double y,
        double width,
        double height)
    {
        writer.WriteAttributeString("x", Format(x));
        writer.WriteAttributeString("y", Format(y));
        writer.WriteAttributeString("width", Format(width));
        writer.WriteAttributeString("height", Format(height));
    }

    private static void WriteLine(
        XmlWriter writer,
        double x1,
        double y1,
        double x2,
        double y2,
        string? stroke = null,
        double? strokeWidth = null)
    {
        writer.WriteStartElement("line", SvgNamespace);
        writer.WriteAttributeString("x1", Format(x1));
        writer.WriteAttributeString("y1", Format(y1));
        writer.WriteAttributeString("x2", Format(x2));
        writer.WriteAttributeString("y2", Format(y2));
        if (stroke is not null)
        {
            writer.WriteAttributeString("stroke", stroke);
        }

        if (strokeWidth is not null)
        {
            writer.WriteAttributeString("stroke-width", Format(strokeWidth.Value));
        }

        writer.WriteEndElement();
    }

    private static void WriteText(
        XmlWriter writer,
        double x,
        double y,
        string text,
        string fontFamily,
        double fontSize,
        string anchor,
        string? weight = null,
        string color = "#111827")
    {
        writer.WriteStartElement("text", SvgNamespace);
        writer.WriteAttributeString("x", Format(x));
        writer.WriteAttributeString("y", Format(y));
        writer.WriteAttributeString("font-family", fontFamily);
        writer.WriteAttributeString("font-size", Format(fontSize));
        writer.WriteAttributeString("text-anchor", anchor);
        writer.WriteAttributeString("fill", color);
        if (weight is not null)
        {
            writer.WriteAttributeString("font-weight", weight);
        }

        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static string? GetDashArray(LineStyle lineStyle) =>
        lineStyle switch
        {
            LineStyle.Solid or LineStyle.None => null,
            LineStyle.Dashed => "8 5",
            LineStyle.Dotted => "2 4",
            LineStyle.DashDot => "8 4 2 4",
            _ => throw new InvalidOperationException("The line style is not supported."),
        };

    private static string FormatTick(double value, string numberFormat)
    {
        try
        {
            return value.ToString(numberFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"The axis number format '{numberFormat}' is invalid.",
                exception);
        }
    }

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void ValidateOptions(SvgExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FontFamily) || options.FontFamily.Length > 512)
        {
            throw new ArgumentException(
                "The SVG font family must contain between 1 and 512 characters.",
                nameof(options));
        }
    }

    private sealed record MappedPoint(DataPoint Source, double ScreenX, double ScreenY);

    private readonly record struct MappedCoordinate(double X, double Y);
}
