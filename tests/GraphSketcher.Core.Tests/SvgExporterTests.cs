using System.Text;
using System.Xml.Linq;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class SvgExporterTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Fact]
    public void ExportProducesStandaloneParseableSvg()
    {
        var svg = SvgExporter.Export(TestDocumentFactory.Create());
        var xml = XDocument.Parse(svg);

        Assert.Equal(Svg + "svg", xml.Root?.Name);
        Assert.Equal("800", xml.Root?.Attribute("width")?.Value);
        Assert.Equal("graph-title", xml.Root?.Element(Svg + "title")?.Attribute("id")?.Value);
        Assert.Equal(
            "graph-description",
            xml.Root?.Element(Svg + "desc")?.Attribute("id")?.Value);
        Assert.NotEmpty(xml.Descendants(Svg + "path"));
    }

    [Fact]
    public void ExportEscapesUserControlledText()
    {
        var document = TestDocumentFactory.Create();
        document.Title = "<script>alert(\"x\")</script> & Results";
        document.Series[0].Name = "A & B";

        var svg = SvgExporter.Export(document);
        var xml = XDocument.Parse(svg);

        Assert.DoesNotContain("<script>", svg, StringComparison.Ordinal);
        Assert.Equal(document.Title, xml.Root?.Element(Svg + "title")?.Value);
        Assert.Contains("A &amp; B", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportIncludesAreaErrorBarsAnnotationsAndLegend()
    {
        var svg = SvgExporter.Export(TestDocumentFactory.Create());
        var xml = XDocument.Parse(svg);

        Assert.Contains(
            xml.Descendants(Svg + "path"),
            element => element.Attribute("fill-opacity")?.Value == "0.16");
        Assert.NotEmpty(
            xml.Descendants(Svg + "g")
                .Where(element => element.Attribute("class")?.Value == "error-bars")
                .Elements(Svg + "line"));
        Assert.Single(
            xml.Descendants(Svg + "g"),
            element => element.Attribute("class")?.Value == "annotation");
        Assert.Single(
            xml.Descendants(Svg + "g"),
            element => element.Attribute("class")?.Value == "legend");
    }

    [Fact]
    public void LogExportSkipsNonPositivePointsWithoutFailing()
    {
        var document = new GraphDocument
        {
            XAxis = new AxisSettings { Scale = AxisScale.Logarithmic },
            YAxis = new AxisSettings { Scale = AxisScale.Logarithmic },
            Series =
            [
                new GraphSeries
                {
                    Id = "log",
                    Name = "Log",
                    Points =
                    [
                        new DataPoint(-1, 1),
                        new DataPoint(1, 1),
                        new DataPoint(10, 10),
                    ],
                },
            ],
        };

        var svg = SvgExporter.Export(document);
        var xml = XDocument.Parse(svg);

        Assert.Equal(Svg + "svg", xml.Root?.Name);
        Assert.Equal(2, xml.Descendants(Svg + "circle").Count());
    }

    [Fact]
    public async Task AsyncExportWritesUtf8AndLeavesStreamOpen()
    {
        using var stream = new MemoryStream();

        await SvgExporter.ExportAsync(
            TestDocumentFactory.Create(),
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        var output = Encoding.UTF8.GetString(stream.ToArray());

        Assert.StartsWith("<svg", output, StringComparison.Ordinal);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void ExplicitTickSpacingControlsExportedLabels()
    {
        var document = TestDocumentFactory.Create();
        document.XAxis.Minimum = 0;
        document.XAxis.Maximum = 4;
        document.XAxis.TickSpacing = 2;

        var svg = SvgExporter.Export(document);
        var labels = XDocument.Parse(svg)
            .Descendants(Svg + "text")
            .Select(element => element.Value)
            .ToArray();

        Assert.Contains("0", labels);
        Assert.Contains("2", labels);
        Assert.Contains("4", labels);
    }
}
