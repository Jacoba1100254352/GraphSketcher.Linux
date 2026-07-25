using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GraphSketcher.Core.Models;
using GraphPoint = GraphSketcher.Core.Models.DataPoint;

namespace GraphSketcher.App.Controls;

public enum CanvasTool
{
    Select,
    Point,
    Draw,
    Text,
}

public sealed class GraphCoordinatesEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;

    public double Y { get; } = y;
}

public sealed class AnnotationRequestedEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;

    public double Y { get; } = y;
}

/// <summary>
/// Interactive, resolution-independent graph surface.
/// </summary>
public sealed class GraphCanvas : Control
{
    private static readonly Typeface UiTypeface = new("Inter");
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(89, 96, 112));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.FromArgb(38, 98, 105, 122));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(51, 57, 68));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromRgb(103, 80, 216));

    private GraphDocument _document = CreateFallbackDocument();
    private CanvasTool _tool;
    private int _selectedSeriesIndex;
    private readonly HashSet<int> _selectedPointIndices = [];
    private bool _draggingPoints;
    private bool _marqueeActive;
    private Point _pointerDown;
    private Point _lastPointer;
    private Rect _marquee;

    public GraphCanvas()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public event EventHandler? EditStarted;

    public event EventHandler? DocumentChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler<GraphCoordinatesEventArgs>? CoordinatesChanged;

    public event EventHandler<AnnotationRequestedEventArgs>? AnnotationRequested;

    public GraphDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? throw new ArgumentNullException(nameof(value));
            _selectedSeriesIndex = Math.Clamp(_selectedSeriesIndex, 0, Math.Max(0, value.Series.Count - 1));
            _selectedPointIndices.Clear();
            InvalidateVisual();
        }
    }

    public CanvasTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Cursor = value switch
            {
                CanvasTool.Point or CanvasTool.Draw => new Cursor(StandardCursorType.Cross),
                CanvasTool.Text => new Cursor(StandardCursorType.Ibeam),
                _ => new Cursor(StandardCursorType.Arrow),
            };
        }
    }

    public int SelectedSeriesIndex
    {
        get => _selectedSeriesIndex;
        set
        {
            _selectedSeriesIndex = Math.Clamp(value, 0, Math.Max(0, Document.Series.Count - 1));
            _selectedPointIndices.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public IReadOnlyCollection<int> SelectedPointIndices => _selectedPointIndices;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var layout = CreateLayout();
        context.DrawRectangle(ToBrush(Document.Canvas.BackgroundColor), null, new Rect(Bounds.Size));

        if (layout.PlotRect.Width < 10 || layout.PlotRect.Height < 10)
        {
            return;
        }

        DrawGrid(context, layout);
        using (context.PushClip(layout.PlotRect))
        {
            DrawAnnotations(context, layout, behindSeries: true);
            DrawSeries(context, layout);
            DrawAnnotations(context, layout, behindSeries: false);
        }

        DrawAxes(context, layout);
        DrawTitles(context, layout);

        if (Document.Canvas.ShowLegend)
        {
            DrawLegend(context, layout);
        }

        if (_marqueeActive)
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(24, 103, 80, 216)),
                new Pen(SelectionBrush, 1, DashStyle.Dash, PenLineCap.Flat, PenLineJoin.Miter, 10),
                _marquee);
        }
    }

    public void DeleteSelection()
    {
        if (_selectedSeriesIndex < 0 || _selectedSeriesIndex >= Document.Series.Count ||
            _selectedPointIndices.Count == 0)
        {
            return;
        }

        EditStarted?.Invoke(this, EventArgs.Empty);
        var points = Document.Series[_selectedSeriesIndex].Points;
        foreach (var index in _selectedPointIndices.OrderByDescending(index => index))
        {
            if (index >= 0 && index < points.Count)
            {
                points.RemoveAt(index);
            }
        }

        _selectedPointIndices.Clear();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SelectAllPoints()
    {
        _selectedPointIndices.Clear();
        if (_selectedSeriesIndex >= 0 && _selectedSeriesIndex < Document.Series.Count)
        {
            for (var index = 0; index < Document.Series[_selectedSeriesIndex].Points.Count; index++)
            {
                _selectedPointIndices.Add(index);
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void Refresh()
    {
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        var layout = CreateLayout();
        if (!layout.PlotRect.Contains(position))
        {
            return;
        }

        var data = ScreenToData(position, layout);
        switch (Tool)
        {
            case CanvasTool.Point:
                AddPoint(data);
                break;
            case CanvasTool.Draw:
                AddPoint(data);
                if (e.ClickCount >= 2)
                {
                    Tool = CanvasTool.Select;
                }

                break;
            case CanvasTool.Text:
                AnnotationRequested?.Invoke(this, new AnnotationRequestedEventArgs(data.X, data.Y));
                break;
            default:
                BeginSelection(position, e.KeyModifiers, layout);
                if (_draggingPoints || _marqueeActive)
                {
                    e.Pointer.Capture(this);
                }

                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var layout = CreateLayout();

        if (layout.PlotRect.Contains(position))
        {
            var data = ScreenToData(position, layout);
            CoordinatesChanged?.Invoke(this, new GraphCoordinatesEventArgs(data.X, data.Y));
        }

        if (_draggingPoints)
        {
            var before = ScreenToData(_lastPointer, layout);
            var after = ScreenToData(position, layout);
            var deltaX = after.X - before.X;
            var deltaY = after.Y - before.Y;
            MoveSelectedPoints(deltaX, deltaY);
            _lastPointer = position;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_marqueeActive)
        {
            _marquee = RectFromPoints(_pointerDown, position).Intersect(layout.PlotRect);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingPoints)
        {
            _draggingPoints = false;
            e.Pointer.Capture(null);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (_marqueeActive)
        {
            _marqueeActive = false;
            e.Pointer.Capture(null);
            SelectPointsInMarquee(CreateLayout());
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _selectedPointIndices.Clear();
            _marqueeActive = false;
            _draggingPoints = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void AddPoint(GraphPoint point)
    {
        EnsureSeries();
        EditStarted?.Invoke(this, EventArgs.Empty);
        var series = Document.Series[_selectedSeriesIndex];
        series.Points.Add(point);
        _selectedPointIndices.Clear();
        _selectedPointIndices.Add(series.Points.Count - 1);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void EnsureSeries()
    {
        if (Document.Series.Count > 0)
        {
            _selectedSeriesIndex = Math.Clamp(_selectedSeriesIndex, 0, Document.Series.Count - 1);
            return;
        }

        Document.Series.Add(new GraphSeries { Name = "Series 1" });
        _selectedSeriesIndex = 0;
    }

    private void BeginSelection(Point position, KeyModifiers modifiers, PlotLayout layout)
    {
        var hit = FindNearestPoint(position, layout);
        if (hit is { } selected)
        {
            var changedSeries = selected.SeriesIndex != _selectedSeriesIndex;
            if (changedSeries)
            {
                _selectedSeriesIndex = selected.SeriesIndex;
                _selectedPointIndices.Clear();
            }

            if (modifiers.HasFlag(KeyModifiers.Control))
            {
                if (!_selectedPointIndices.Add(selected.PointIndex))
                {
                    _selectedPointIndices.Remove(selected.PointIndex);
                }
            }
            else if (!_selectedPointIndices.Contains(selected.PointIndex))
            {
                _selectedPointIndices.Clear();
                _selectedPointIndices.Add(selected.PointIndex);
            }

            if (_selectedPointIndices.Count > 0)
            {
                EditStarted?.Invoke(this, EventArgs.Empty);
                _draggingPoints = true;
                _lastPointer = position;
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (!modifiers.HasFlag(KeyModifiers.Control))
        {
            _selectedPointIndices.Clear();
        }

        _pointerDown = position;
        _marquee = new Rect(position, new Size());
        _marqueeActive = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveSelectedPoints(double deltaX, double deltaY)
    {
        if (_selectedSeriesIndex < 0 || _selectedSeriesIndex >= Document.Series.Count)
        {
            return;
        }

        var series = Document.Series[_selectedSeriesIndex];
        foreach (var index in _selectedPointIndices)
        {
            if (index < 0 || index >= series.Points.Count)
            {
                continue;
            }

            var point = series.Points[index];
            var nextX = point.X + deltaX;
            var nextY = point.Y + deltaY;
            if (Document.XAxis.Scale == AxisScale.Logarithmic && nextX <= 0 ||
                Document.YAxis.Scale == AxisScale.Logarithmic && nextY <= 0)
            {
                continue;
            }

            point.X = nextX;
            point.Y = nextY;
        }
    }

    private void SelectPointsInMarquee(PlotLayout layout)
    {
        if (_selectedSeriesIndex < 0 || _selectedSeriesIndex >= Document.Series.Count)
        {
            return;
        }

        var points = Document.Series[_selectedSeriesIndex].Points;
        for (var index = 0; index < points.Count; index++)
        {
            if (TryDataToScreen(points[index], layout, out var screen) && _marquee.Contains(screen))
            {
                _selectedPointIndices.Add(index);
            }
        }
    }

    private PointHit? FindNearestPoint(Point pointer, PlotLayout layout)
    {
        const double hitRadiusSquared = 12 * 12;
        PointHit? nearest = null;
        var nearestDistance = hitRadiusSquared;

        for (var seriesIndex = Document.Series.Count - 1; seriesIndex >= 0; seriesIndex--)
        {
            var series = Document.Series[seriesIndex];
            if (!series.IsVisible)
            {
                continue;
            }

            for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
            {
                if (!TryDataToScreen(series.Points[pointIndex], layout, out var screen))
                {
                    continue;
                }

                var delta = pointer - screen;
                var distance = (delta.X * delta.X) + (delta.Y * delta.Y);
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = new PointHit(seriesIndex, pointIndex);
                }
            }
        }

        return nearest;
    }

    private void DrawGrid(DrawingContext context, PlotLayout layout)
    {
        if (Document.XAxis.ShowGridLines)
        {
            foreach (var tick in CreateTicks(Document.XAxis, layout.XMinimum, layout.XMaximum))
            {
                if (TryDataToScreen(new GraphPoint(tick, layout.YMinimum), layout, out var point))
                {
                    context.DrawLine(new Pen(GridBrush, 1), new Point(point.X, layout.PlotRect.Top), new Point(point.X, layout.PlotRect.Bottom));
                }
            }
        }

        if (Document.YAxis.ShowGridLines)
        {
            foreach (var tick in CreateTicks(Document.YAxis, layout.YMinimum, layout.YMaximum))
            {
                if (TryDataToScreen(new GraphPoint(layout.XMinimum, tick), layout, out var point))
                {
                    context.DrawLine(new Pen(GridBrush, 1), new Point(layout.PlotRect.Left, point.Y), new Point(layout.PlotRect.Right, point.Y));
                }
            }
        }
    }

    private void DrawAxes(DrawingContext context, PlotLayout layout)
    {
        var axisPen = new Pen(AxisBrush, 1.5);
        var xAxisY = layout.YMinimum <= 0 && layout.YMaximum >= 0 && Document.YAxis.Scale == AxisScale.Linear
            ? DataToScreen(new GraphPoint(layout.XMinimum, 0), layout).Y
            : layout.PlotRect.Bottom;
        var yAxisX = layout.XMinimum <= 0 && layout.XMaximum >= 0 && Document.XAxis.Scale == AxisScale.Linear
            ? DataToScreen(new GraphPoint(0, layout.YMinimum), layout).X
            : layout.PlotRect.Left;

        if (Document.XAxis.ShowAxisLine)
        {
            context.DrawLine(axisPen, new Point(layout.PlotRect.Left, xAxisY), new Point(layout.PlotRect.Right, xAxisY));
        }

        if (Document.YAxis.ShowAxisLine)
        {
            context.DrawLine(axisPen, new Point(yAxisX, layout.PlotRect.Top), new Point(yAxisX, layout.PlotRect.Bottom));
        }

        if (Document.XAxis.ShowTickLabels)
        {
            foreach (var tick in CreateTicks(Document.XAxis, layout.XMinimum, layout.XMaximum))
            {
                var screen = DataToScreen(new GraphPoint(tick, layout.YMinimum), layout);
                if (Document.XAxis.ShowAxisLine)
                {
                    context.DrawLine(axisPen, new Point(screen.X, xAxisY - 3), new Point(screen.X, xAxisY + 3));
                }

                var label = CreateText(FormatTick(tick, Document.XAxis), 11, MutedTextBrush);
                context.DrawText(label, new Point(screen.X - (label.Width / 2), layout.PlotRect.Bottom + 8));
            }
        }

        if (Document.YAxis.ShowTickLabels)
        {
            foreach (var tick in CreateTicks(Document.YAxis, layout.YMinimum, layout.YMaximum))
            {
                var screen = DataToScreen(new GraphPoint(layout.XMinimum, tick), layout);
                if (Document.YAxis.ShowAxisLine)
                {
                    context.DrawLine(axisPen, new Point(yAxisX - 3, screen.Y), new Point(yAxisX + 3, screen.Y));
                }

                var label = CreateText(FormatTick(tick, Document.YAxis), 11, MutedTextBrush);
                context.DrawText(label, new Point(layout.PlotRect.Left - label.Width - 9, screen.Y - (label.Height / 2)));
            }
        }
    }

    private void DrawTitles(DrawingContext context, PlotLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(Document.Title))
        {
            var title = CreateText(Document.Title, 20, AxisBrush, FontWeight.SemiBold);
            context.DrawText(title, new Point(
                layout.PlotRect.Center.X - (title.Width / 2),
                Math.Max(10, layout.PlotRect.Top - 38)));
        }

        if (!string.IsNullOrWhiteSpace(Document.XAxis.Title))
        {
            var title = CreateText(Document.XAxis.Title, 13, AxisBrush, FontWeight.SemiBold);
            context.DrawText(title, new Point(
                layout.PlotRect.Center.X - (title.Width / 2),
                layout.PlotRect.Bottom + 35));
        }

        if (!string.IsNullOrWhiteSpace(Document.YAxis.Title))
        {
            var title = CreateText(Document.YAxis.Title, 13, AxisBrush, FontWeight.SemiBold);
            context.DrawText(title, new Point(
                Math.Max(4, layout.PlotRect.Left - title.Width - 9),
                Math.Max(4, layout.PlotRect.Top - 22)));
        }
    }

    private void DrawSeries(DrawingContext context, PlotLayout layout)
    {
        for (var seriesIndex = 0; seriesIndex < Document.Series.Count; seriesIndex++)
        {
            var series = Document.Series[seriesIndex];
            if (!series.IsVisible || series.Points.Count == 0)
            {
                continue;
            }

            var brush = ToBrush(series.Color);
            var pen = CreateSeriesPen(series, brush);
            var points = series.Points
                .Select((point, index) => (Point: point, Index: index))
                .Where(item => TryDataToScreen(item.Point, layout, out _))
                .Select(item => (Screen: DataToScreen(item.Point, layout), item.Point, item.Index))
                .ToList();

            if (series.FillArea && series.LineMode != LineMode.None && points.Count > 1)
            {
                DrawAreaFill(context, layout, series, points, brush);
            }

            if (series.LineStyle != LineStyle.None && series.LineMode != LineMode.None && points.Count > 1)
            {
                DrawConnectedLine(context, series.LineMode, points.Select(item => item.Screen).ToList(), pen);
            }

            foreach (var item in points)
            {
                DrawErrorBars(context, item.Point, item.Screen, layout, pen);
                if (series.MarkerShape != MarkerShape.None)
                {
                    DrawMarker(context, series.MarkerShape, item.Screen, series.MarkerSize, brush, pen);
                }

                if (!string.IsNullOrWhiteSpace(item.Point.Label))
                {
                    var label = CreateText(item.Point.Label!, 11, brush);
                    context.DrawText(label, new Point(item.Screen.X + 6, item.Screen.Y - label.Height - 5));
                }

                if (seriesIndex == _selectedSeriesIndex && _selectedPointIndices.Contains(item.Index))
                {
                    context.DrawEllipse(null, new Pen(SelectionBrush, 2), item.Screen, Math.Max(7, series.MarkerSize + 4), Math.Max(7, series.MarkerSize + 4));
                }
            }
        }
    }

    private static void DrawConnectedLine(
        DrawingContext context,
        LineMode lineMode,
        List<Point> points,
        Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], false);
            switch (lineMode)
            {
                case LineMode.Step:
                    for (var index = 1; index < points.Count; index++)
                    {
                        path.LineTo(new Point(points[index].X, points[index - 1].Y));
                        path.LineTo(points[index]);
                    }

                    break;
                case LineMode.Smooth:
                    for (var index = 0; index < points.Count - 1; index++)
                    {
                        var p0 = index == 0 ? points[index] : points[index - 1];
                        var p1 = points[index];
                        var p2 = points[index + 1];
                        var p3 = index + 2 < points.Count ? points[index + 2] : p2;
                        var control1 = p1 + ((p2 - p0) / 6);
                        var control2 = p2 - ((p3 - p1) / 6);
                        path.CubicBezierTo(control1, control2, p2);
                    }

                    break;
                default:
                    for (var index = 1; index < points.Count; index++)
                    {
                        path.LineTo(points[index]);
                    }

                    break;
            }

            path.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawAreaFill(
        DrawingContext context,
        PlotLayout layout,
        GraphSeries series,
        List<(Point Screen, GraphPoint Point, int Index)> points,
        IBrush seriesBrush)
    {
        var baselineValue = layout.YMinimum <= 0 && layout.YMaximum >= 0 ? 0 : layout.YMinimum;
        var baselineY = DataToScreen(new GraphPoint(layout.XMinimum, baselineValue), layout).Y;
        var color = ((SolidColorBrush)seriesBrush).Color;
        var fill = new SolidColorBrush(Color.FromArgb(45, color.R, color.G, color.B));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(points[0].Screen.X, baselineY), true);
            path.LineTo(points[0].Screen);
            foreach (var item in points.Skip(1))
            {
                path.LineTo(item.Screen);
            }

            path.LineTo(new Point(points[^1].Screen.X, baselineY));
            path.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);
    }

    private static void DrawErrorBars(
        DrawingContext context,
        GraphPoint point,
        Point screen,
        PlotLayout layout,
        Pen pen)
    {
        if (point.XError is > 0)
        {
            var left = DataToScreen(new GraphPoint(point.X - point.XError.Value, point.Y), layout);
            var right = DataToScreen(new GraphPoint(point.X + point.XError.Value, point.Y), layout);
            context.DrawLine(pen, left, right);
            context.DrawLine(pen, new Point(left.X, screen.Y - 4), new Point(left.X, screen.Y + 4));
            context.DrawLine(pen, new Point(right.X, screen.Y - 4), new Point(right.X, screen.Y + 4));
        }

        if (point.YError is > 0)
        {
            var bottom = DataToScreen(new GraphPoint(point.X, point.Y - point.YError.Value), layout);
            var top = DataToScreen(new GraphPoint(point.X, point.Y + point.YError.Value), layout);
            context.DrawLine(pen, bottom, top);
            context.DrawLine(pen, new Point(screen.X - 4, bottom.Y), new Point(screen.X + 4, bottom.Y));
            context.DrawLine(pen, new Point(screen.X - 4, top.Y), new Point(screen.X + 4, top.Y));
        }
    }

    private static void DrawMarker(
        DrawingContext context,
        MarkerShape shape,
        Point center,
        double markerSize,
        IBrush brush,
        Pen pen)
    {
        var radius = Math.Max(1.5, markerSize / 2);
        switch (shape)
        {
            case MarkerShape.Circle:
                context.DrawEllipse(brush, null, center, radius, radius);
                break;
            case MarkerShape.Square:
                context.DrawRectangle(brush, null, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                break;
            case MarkerShape.Triangle:
                DrawPolygon(context, brush, null,
                [
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius, center.Y + radius),
                    new Point(center.X - radius, center.Y + radius),
                ]);
                break;
            case MarkerShape.Diamond:
                DrawPolygon(context, brush, null,
                [
                    new Point(center.X, center.Y - radius),
                    new Point(center.X + radius, center.Y),
                    new Point(center.X, center.Y + radius),
                    new Point(center.X - radius, center.Y),
                ]);
                break;
            case MarkerShape.Cross:
                context.DrawLine(pen, new Point(center.X - radius, center.Y - radius), new Point(center.X + radius, center.Y + radius));
                context.DrawLine(pen, new Point(center.X - radius, center.Y + radius), new Point(center.X + radius, center.Y - radius));
                break;
            case MarkerShape.Plus:
                context.DrawLine(pen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
                context.DrawLine(pen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
                break;
        }
    }

    private void DrawAnnotations(DrawingContext context, PlotLayout layout, bool behindSeries)
    {
        foreach (var annotation in Document.Annotations)
        {
            var isShape = annotation.Kind != AnnotationKind.Text;
            if (isShape != behindSeries)
            {
                continue;
            }

            var start = annotation.CoordinateSpace == AnnotationCoordinateSpace.Data
                ? DataToScreen(new GraphPoint(annotation.X, annotation.Y), layout)
                : new Point(annotation.X, annotation.Y);
            var brush = ToBrush(annotation.Color);
            var fillBrush = ToBrush(annotation.FillColor);
            var pen = new Pen(brush, annotation.StrokeWidth, null, PenLineCap.Round, PenLineJoin.Round, 10);

            if (annotation.Kind == AnnotationKind.Text)
            {
                var text = CreateText(annotation.Text, annotation.FontSize, brush);
                context.DrawText(text, start);
                continue;
            }

            if (annotation.X2 is not { } x2 || annotation.Y2 is not { } y2)
            {
                continue;
            }

            var end = annotation.CoordinateSpace == AnnotationCoordinateSpace.Data
                ? DataToScreen(new GraphPoint(x2, y2), layout)
                : new Point(x2, y2);
            var bounds = RectFromPoints(start, end);
            switch (annotation.Kind)
            {
                case AnnotationKind.Line:
                    context.DrawLine(pen, start, end);
                    break;
                case AnnotationKind.Arrow:
                    context.DrawLine(pen, start, end);
                    DrawArrowHead(context, start, end, brush);
                    break;
                case AnnotationKind.Rectangle:
                    context.DrawRectangle(fillBrush, pen, bounds);
                    break;
                case AnnotationKind.Ellipse:
                    context.DrawEllipse(fillBrush, pen, bounds);
                    break;
            }
        }
    }

    private void DrawLegend(DrawingContext context, PlotLayout layout)
    {
        var visible = Document.Series.Where(series => series.IsVisible).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        const double rowHeight = 22;
        var width = Math.Clamp(
            visible.Select(series => CreateText(series.Name, 11, AxisBrush).Width).DefaultIfEmpty(60).Max() + 50,
            110,
            230);
        var height = (visible.Count * rowHeight) + 16;
        var left = Document.Canvas.LegendPosition switch
        {
            LegendPosition.TopLeft or LegendPosition.BottomLeft => layout.PlotRect.Left + 10,
            _ => layout.PlotRect.Right - width - 10,
        };
        var top = Document.Canvas.LegendPosition switch
        {
            LegendPosition.BottomLeft or LegendPosition.BottomRight => layout.PlotRect.Bottom - height - 10,
            _ => layout.PlotRect.Top + 10,
        };
        var bounds = new Rect(left, top, width, height);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(60, 41, 48, 63)), 1),
            bounds,
            6,
            6);

        for (var index = 0; index < visible.Count; index++)
        {
            var series = visible[index];
            var y = top + 9 + (index * rowHeight) + (rowHeight / 2);
            var brush = ToBrush(series.Color);
            context.DrawLine(new Pen(brush, 2.5), new Point(left + 11, y), new Point(left + 32, y));
            DrawMarker(context, series.MarkerShape, new Point(left + 21.5, y), Math.Max(5, series.MarkerSize), brush, new Pen(brush, 1.5));
            var text = CreateText(series.Name, 11, AxisBrush);
            context.DrawText(text, new Point(left + 40, y - (text.Height / 2)));
        }
    }

    private PlotLayout CreateLayout()
    {
        const double left = 78;
        const double right = 28;
        var top = string.IsNullOrWhiteSpace(Document.Title) ? 30 : 52;
        const double bottom = 62;
        var plotRect = new Rect(
            left,
            top,
            Math.Max(0, Bounds.Width - left - right),
            Math.Max(0, Bounds.Height - top - bottom));

        var xRange = ResolveRange(Document.XAxis, isX: true);
        var yRange = ResolveRange(Document.YAxis, isX: false);
        return new PlotLayout(plotRect, xRange.Minimum, xRange.Maximum, yRange.Minimum, yRange.Maximum)
        {
            XLog = Document.XAxis.Scale == AxisScale.Logarithmic,
            YLog = Document.YAxis.Scale == AxisScale.Logarithmic,
            XLogBase = Document.XAxis.LogarithmBase,
            YLogBase = Document.YAxis.LogarithmBase,
        };
    }

    private (double Minimum, double Maximum) ResolveRange(AxisSettings axis, bool isX)
    {
        var values = Document.Series
            .Where(series => series.IsVisible)
            .SelectMany(series => series.Points)
            .Select(point => isX ? point.X : point.Y)
            .Where(double.IsFinite)
            .Where(value => axis.Scale != AxisScale.Logarithmic || value > 0)
            .ToList();

        var dataMinimum = values.Count > 0 ? values.Min() : (axis.Scale == AxisScale.Logarithmic ? 1 : 0);
        var dataMaximum = values.Count > 0 ? values.Max() : (axis.Scale == AxisScale.Logarithmic ? 10 : 10);
        var minimum = axis.Minimum ?? dataMinimum;
        var maximum = axis.Maximum ?? dataMaximum;

        if (axis.Scale == AxisScale.Logarithmic)
        {
            minimum = minimum > 0 ? minimum : values.Where(value => value > 0).DefaultIfEmpty(1).Min();
            maximum = maximum > minimum ? maximum : minimum * axis.LogarithmBase;
            if (axis.Minimum is null)
            {
                minimum = Math.Pow(axis.LogarithmBase, Math.Floor(Math.Log(minimum, axis.LogarithmBase)));
            }

            if (axis.Maximum is null)
            {
                maximum = Math.Pow(axis.LogarithmBase, Math.Ceiling(Math.Log(maximum, axis.LogarithmBase)));
            }
        }
        else
        {
            if (Math.Abs(maximum - minimum) < double.Epsilon)
            {
                var equalValuePadding = Math.Abs(minimum) > 0 ? Math.Abs(minimum) * 0.1 : 1;
                minimum -= equalValuePadding;
                maximum += equalValuePadding;
            }

            var padding = (maximum - minimum) * 0.06;
            if (axis.Minimum is null)
            {
                minimum -= padding;
            }

            if (axis.Maximum is null)
            {
                maximum += padding;
            }
        }

        return axis.IsReversed ? (maximum, minimum) : (minimum, maximum);
    }

    private static List<double> CreateTicks(AxisSettings axis, double minimum, double maximum)
    {
        var low = Math.Min(minimum, maximum);
        var high = Math.Max(minimum, maximum);
        if (!double.IsFinite(low) || !double.IsFinite(high) || low == high)
        {
            return [];
        }

        if (axis.Scale == AxisScale.Logarithmic)
        {
            var start = (int)Math.Ceiling(Math.Log(low, axis.LogarithmBase));
            var end = (int)Math.Floor(Math.Log(high, axis.LogarithmBase));
            var result = new List<double>();
            for (var exponent = start; exponent <= end && result.Count < 100; exponent++)
            {
                result.Add(Math.Pow(axis.LogarithmBase, exponent));
            }

            return result;
        }

        var step = axis.TickSpacing is > 0
            ? axis.TickSpacing.Value
            : NiceStep((high - low) / Math.Max(2, axis.DesiredTickCount - 1));
        if (!double.IsFinite(step) || step <= 0)
        {
            return [];
        }

        var first = Math.Ceiling(low / step) * step;
        var ticks = new List<double>();
        for (var value = first; value <= high + (step * 1e-9) && ticks.Count < 100; value += step)
        {
            ticks.Add(Math.Abs(value) < step * 1e-12 ? 0 : value);
        }

        return ticks;
    }

    private static double NiceStep(double roughStep)
    {
        if (!double.IsFinite(roughStep) || roughStep <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var normalized = roughStep / magnitude;
        var nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };
        return nice * magnitude;
    }

    private static string FormatTick(double value, AxisSettings axis)
    {
        try
        {
            return value.ToString(axis.NumberFormat, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return value.ToString("G4", CultureInfo.CurrentCulture);
        }
    }

    private static bool TryDataToScreen(GraphPoint point, PlotLayout layout, out Point screen)
    {
        screen = default;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X <= 0 && layout.XLog ||
            point.Y <= 0 && layout.YLog)
        {
            return false;
        }

        screen = DataToScreen(point, layout);
        return double.IsFinite(screen.X) && double.IsFinite(screen.Y);
    }

    private static Point DataToScreen(GraphPoint point, PlotLayout layout)
    {
        var x = Normalize(point.X, layout.XMinimum, layout.XMaximum, layout.XLog, layout.XLogBase);
        var y = Normalize(point.Y, layout.YMinimum, layout.YMaximum, layout.YLog, layout.YLogBase);
        return new Point(
            layout.PlotRect.Left + (x * layout.PlotRect.Width),
            layout.PlotRect.Bottom - (y * layout.PlotRect.Height));
    }

    private GraphPoint ScreenToData(Point point, PlotLayout layout)
    {
        var normalizedX = (point.X - layout.PlotRect.Left) / layout.PlotRect.Width;
        var normalizedY = (layout.PlotRect.Bottom - point.Y) / layout.PlotRect.Height;
        return new GraphPoint(
            Denormalize(normalizedX, layout.XMinimum, layout.XMaximum, Document.XAxis.Scale, Document.XAxis.LogarithmBase),
            Denormalize(normalizedY, layout.YMinimum, layout.YMaximum, Document.YAxis.Scale, Document.YAxis.LogarithmBase));
    }

    private static double Normalize(double value, double minimum, double maximum, bool logarithmic, double logBase)
    {
        if (logarithmic)
        {
            value = Math.Log(value, logBase);
            minimum = Math.Log(minimum, logBase);
            maximum = Math.Log(maximum, logBase);
        }

        return (value - minimum) / (maximum - minimum);
    }

    private static double Denormalize(
        double value,
        double minimum,
        double maximum,
        AxisScale scale,
        double logBase)
    {
        if (scale == AxisScale.Logarithmic)
        {
            var low = Math.Log(minimum, logBase);
            var high = Math.Log(maximum, logBase);
            return Math.Pow(logBase, low + (value * (high - low)));
        }

        return minimum + (value * (maximum - minimum));
    }

    private static Pen CreateSeriesPen(GraphSeries series, IBrush brush)
    {
        IDashStyle? dash = series.LineStyle switch
        {
            LineStyle.Dashed => DashStyle.Dash,
            LineStyle.Dotted => DashStyle.Dot,
            LineStyle.DashDot => DashStyle.DashDot,
            _ => null,
        };
        return new Pen(
            brush,
            Math.Max(0.5, series.StrokeWidth),
            dash,
            PenLineCap.Round,
            PenLineJoin.Round,
            10);
    }

    private static SolidColorBrush ToBrush(string? cssColor)
    {
        return new SolidColorBrush(ParseCssColor(cssColor));
    }

    private static Color ParseCssColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Colors.Black;
        }

        var text = value.TrimStart('#');
        try
        {
            return text.Length switch
            {
                3 => Color.FromRgb(
                    Convert.ToByte(new string(text[0], 2), 16),
                    Convert.ToByte(new string(text[1], 2), 16),
                    Convert.ToByte(new string(text[2], 2), 16)),
                4 => Color.FromArgb(
                    Convert.ToByte(new string(text[3], 2), 16),
                    Convert.ToByte(new string(text[0], 2), 16),
                    Convert.ToByte(new string(text[1], 2), 16),
                    Convert.ToByte(new string(text[2], 2), 16)),
                6 => Color.FromRgb(
                    Convert.ToByte(text[..2], 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(text.Substring(6, 2), 16),
                    Convert.ToByte(text[..2], 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16)),
                _ => Colors.Black,
            };
        }
        catch (FormatException)
        {
            return Colors.Black;
        }
    }

    private static FormattedText CreateText(
        string text,
        double size,
        IBrush brush,
        FontWeight? weight = null)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(UiTypeface.FontFamily, FontStyle.Normal, weight ?? FontWeight.Normal),
            size,
            brush);
    }

    private static void DrawPolygon(
        DrawingContext context,
        IBrush? fill,
        Pen? pen,
        IReadOnlyList<Point> points)
    {
        if (points.Count < 3)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], fill is not null);
            for (var index = 1; index < points.Count; index++)
            {
                path.LineTo(points[index]);
            }

            path.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawArrowHead(DrawingContext context, Point start, Point end, IBrush brush)
    {
        var direction = end - start;
        var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        if (length < 1)
        {
            return;
        }

        var unit = direction / length;
        var normal = new Vector(-unit.Y, unit.X);
        DrawPolygon(context, brush, null,
        [
            end,
            end - (unit * 11) + (normal * 5),
            end - (unit * 11) - (normal * 5),
        ]);
    }

    private static Rect RectFromPoints(Point first, Point second)
    {
        return new Rect(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
    }

    private static GraphDocument CreateFallbackDocument()
    {
        return new GraphDocument
        {
            Title = "Untitled Graph",
            Series =
            [
                new GraphSeries
                {
                    Name = "Series 1",
                    Points = [new GraphPoint(0, 0), new GraphPoint(1, 1)],
                },
            ],
        };
    }

    private readonly record struct PointHit(int SeriesIndex, int PointIndex);

    private readonly record struct PlotLayout(
        Rect PlotRect,
        double XMinimum,
        double XMaximum,
        double YMinimum,
        double YMaximum)
    {
        public bool XLog { get; init; }

        public bool YLog { get; init; }

        public double XLogBase { get; init; } = 10;

        public double YLogBase { get; init; } = 10;
    }
}
