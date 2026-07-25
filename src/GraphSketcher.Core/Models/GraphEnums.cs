namespace GraphSketcher.Core.Models;

/// <summary>
/// Defines how values are mapped onto an axis.
/// </summary>
public enum AxisScale
{
    Linear,
    Logarithmic,
}

/// <summary>
/// Defines the stroke pattern used to draw a series.
/// </summary>
public enum LineStyle
{
    None,
    Solid,
    Dashed,
    Dotted,
    DashDot,
}

/// <summary>
/// Defines how adjacent data points are connected.
/// </summary>
public enum LineMode
{
    None,
    Straight,
    Step,
    Smooth,
}

/// <summary>
/// Defines the symbol drawn at each data point.
/// </summary>
public enum MarkerShape
{
    None,
    Circle,
    Square,
    Triangle,
    Diamond,
    Cross,
    Plus,
}

/// <summary>
/// Defines where the series legend is anchored inside the plot.
/// </summary>
public enum LegendPosition
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

/// <summary>
/// Defines the visual represented by a graph annotation.
/// </summary>
public enum AnnotationKind
{
    Text,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

/// <summary>
/// Defines whether annotation coordinates are graph values or canvas pixels.
/// </summary>
public enum AnnotationCoordinateSpace
{
    Data,
    Canvas,
}
