using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Tests;

public sealed class ModelValidationTests
{
    [Fact]
    public void DefaultDocumentIsValid()
    {
        var document = new GraphDocument();

        Assert.Empty(document.Validate());
    }

    [Fact]
    public void ValidateReportsNestedModelErrors()
    {
        var document = TestDocumentFactory.Create();
        document.Canvas.Width = double.NaN;
        document.XAxis.Minimum = 10;
        document.XAxis.Maximum = 2;
        document.Series[0].Points[0].YError = -1;
        document.Annotations[0].X2 = null;

        var errors = document.Validate();

        Assert.Contains(errors, error => error.Contains("canvas.width", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("xAxis.minimum", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("yError", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("x2", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsDuplicateObjectIdentifiers()
    {
        var document = TestDocumentFactory.Create();
        document.Series.Add(new GraphSeries { Id = "revenue", Name = "Duplicate" });
        document.Annotations.Add(new GraphAnnotation
        {
            Id = "target",
            Kind = AnnotationKind.Text,
            Text = "Duplicate",
        });

        var errors = document.Validate();

        Assert.Contains(errors, error => error.Contains("duplicates another series", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicates another annotation", StringComparison.Ordinal));
    }

    [Fact]
    public void LogAxisRequiresPositiveBoundsAndValidBase()
    {
        var document = new GraphDocument
        {
            XAxis = new AxisSettings
            {
                Scale = AxisScale.Logarithmic,
                Minimum = 0,
                Maximum = 100,
                LogarithmBase = 1,
            },
        };

        var errors = document.Validate();

        Assert.Contains(errors, error => error.Contains("greater than zero", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("logarithmBase", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#123")]
    [InlineData("#1234")]
    [InlineData("#123456")]
    [InlineData("#12345678")]
    public void SupportedHexColorsAreValid(string color)
    {
        var document = new GraphDocument();
        document.Canvas.BackgroundColor = color;

        Assert.Empty(document.Validate());
    }

    [Fact]
    public void RequestedPresentationFieldsAreValidated()
    {
        var document = TestDocumentFactory.Create();
        document.Canvas.LegendPosition = (LegendPosition)999;
        document.XAxis.TickSpacing = -0.5;
        document.Series[0].Points[0].XError = double.PositiveInfinity;

        var errors = document.Validate();

        Assert.Contains(errors, error => error.Contains("legendPosition", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("tickSpacing", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("xError", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationHandlesNullsProducedByMalformedJsonModels()
    {
        var document = TestDocumentFactory.Create();
        document.XAxis.Title = null!;
        document.Annotations[0].Text = null!;

        var errors = document.Validate();

        Assert.Contains(errors, error => error.Contains("xAxis.title", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("annotations[0].text", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidNumericFormatIsRejectedBeforeRendering()
    {
        var document = new GraphDocument();
        document.XAxis.NumberFormat = "Q";

        Assert.Contains(
            document.Validate(),
            error => error.Contains("numberFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureValidThrowsOneActionableException()
    {
        var document = new GraphDocument { Title = string.Empty };

        var exception = Assert.Throws<InvalidDataException>(document.EnsureValid);

        Assert.Contains("title", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsSeriesAboveResourceCap()
    {
        var document = new GraphDocument
        {
            Series = Enumerable
                .Repeat(new GraphSeries(), GraphDocument.MaximumSeriesCount + 1)
                .ToList(),
        };

        var errors = document.Validate();

        Assert.Contains(
            errors,
            error => error.Contains("256 items", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsAggregatePointsAndAnnotationsAboveResourceCaps()
    {
        var document = new GraphDocument
        {
            Series =
            [
                new GraphSeries
                {
                    Points = Enumerable
                        .Repeat(
                            new DataPoint(),
                            GraphDocument.MaximumTotalPointCount + 1)
                        .ToList(),
                },
            ],
            Annotations = Enumerable
                .Repeat(
                    new GraphAnnotation(),
                    GraphDocument.MaximumAnnotationCount + 1)
                .ToList(),
        };

        var errors = document.Validate();

        Assert.Contains(
            errors,
            error => error.Contains("250,000 total points", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains("10,000 items", StringComparison.Ordinal));
    }
}
