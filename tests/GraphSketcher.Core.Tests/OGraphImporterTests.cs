using System.IO.Compression;
using System.Text;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;
using Xunit;

namespace GraphSketcher.Core.Tests;

public sealed class OGraphImporterTests
{
    private const string NamespaceV1 =
        "http://www.omnigroup.com/namespace/OmniGraphSketcher/v1";

    [Fact]
    public void ImportPlainXmlPreservesSupportedGraphContent()
    {
        var xml = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <document xmlns="{{NamespaceV1}}">
              <graph>
                <canvas w="800" h="600">
                  <color r="0.2" g="0.4" b="0.6" a="0.5"/>
                  <whitespace top="11" right="12" bottom="13" left="14"/>
                  <edge-padding top="6" right="6" bottom="6" left="6"/>
                </canvas>
                <axis id="ax" dimension="x" min="-2" max="8" visible="true">
                  <ticks spacing="2" visible="true"/>
                  <grid spacing="2" visible="true"/>
                  <tick-labels visible="true" scientific-notation="off">
                    <user-labels>
                      <label tick="2" idref="tick-label"/>
                    </user-labels>
                  </tick-labels>
                  <title label="x-title" visible="true"/>
                </axis>
                <axis id="ay" dimension="y" min="1" max="100" scale="logarithmic">
                  <ticks spacing="1" user-spacing="1"/>
                  <grid spacing="1" visible="false"/>
                  <tick-labels visible="true" scientific-notation="on"/>
                  <title label="y-title" visible="true"/>
                </axis>
                <vertex id="v1" x="3" y="2" width="4" shape="circle">
                  <color r="1" g="0" b="0"/>
                  <snapped-to><element idref="line-1" param="0.5"/></snapped-to>
                </vertex>
                <vertex id="v2" x="1" y="4" width="4" shape="circle">
                  <color r="1" g="0" b="0"/>
                </vertex>
                <vertex id="v3" x="2" y="3" width="4" shape="circle">
                  <color r="1" g="0" b="0"/>
                </vertex>
                <vertex id="free" x="7" y="9" width="7" shape="cross">
                  <color r="0" g="0" b="1"/>
                </vertex>
                <line id="line-1" class="connect" method="curved" width="3" dash="dashes-dots">
                  <color r="1" g="0" b="0"/>
                  <vertices ids="v2 v3 v1"/>
                </line>
                <fill id="fill-1">
                  <color r="0" g="0.5" b="1" a="0.5"/>
                  <vertices ids="v1 v3 v2"/>
                </fill>
                <label id="x-title" x="3" y="-1">
                  <text><p>
                    <run><style><value key="font-weight">9</value></style><lit>Time </lit></run>
                    <run><lit>axis</lit></run>
                  </p></text>
                </label>
                <label id="y-title" x="-1" y="50">
                  <text><p><run><lit>Value axis</lit></run></p></text>
                </label>
                <label id="tick-label" x="2" y="0">
                  <text><p><run><lit>two</lit></run></p></text>
                </label>
                <label id="point-label" owner="v2">
                  <text><p><run><lit>middle point</lit></run></p></text>
                </label>
                <label id="line-label" owner="line-1">
                  <text><p><run><lit>Observed</lit></run></p></text>
                </label>
                <label id="fill-label" owner="fill-1">
                  <text><p><run><lit>Region</lit></run></p></text>
                </label>
                <label id="note" x="6" y="7">
                  <text><p><run>
                    <style>
                      <value key="font-size">18</value>
                      <value key="font-fill"><color r="0" g="0.5" b="0"/></value>
                    </style>
                    <lit>Remember this</lit>
                  </run></p></text>
                </label>
                <group id="group-1" elements="v1 v2 v3"/>
              </graph>
            </document>
            """;

        var result = OGraphImporter.Import(Utf8(xml), "experiment.ograph");
        var document = result.Document;

        Assert.Equal("experiment", document.Title);
        Assert.Equal(800, document.Canvas.Width);
        Assert.Equal(600, document.Canvas.Height);
        Assert.Equal(11, document.Canvas.PaddingTop);
        Assert.Equal(12, document.Canvas.PaddingRight);
        Assert.Equal(13, document.Canvas.PaddingBottom);
        Assert.Equal(14, document.Canvas.PaddingLeft);
        Assert.Equal("#33669980", document.Canvas.BackgroundColor);

        Assert.Equal("Time axis", document.XAxis.Title);
        Assert.Equal(-2, document.XAxis.Minimum);
        Assert.Equal(8, document.XAxis.Maximum);
        Assert.Equal(2, document.XAxis.TickSpacing);
        Assert.Equal(6, document.XAxis.DesiredTickCount);
        Assert.True(document.XAxis.ShowGridLines);
        Assert.Equal("0.####", document.XAxis.NumberFormat);

        Assert.Equal(AxisScale.Logarithmic, document.YAxis.Scale);
        Assert.Equal(1, document.YAxis.Minimum);
        Assert.Equal(100, document.YAxis.Maximum);
        Assert.Equal(1, document.YAxis.TickSpacing);
        Assert.Equal(3, document.YAxis.DesiredTickCount);
        Assert.False(document.YAxis.ShowGridLines);
        Assert.Equal("Value axis", document.YAxis.Title);
        Assert.Equal("0.###E+0", document.YAxis.NumberFormat);

        var line = Assert.Single(document.Series, item => item.Id == "line-1");
        Assert.Equal("Observed", line.Name);
        Assert.Equal(LineStyle.DashDot, line.LineStyle);
        Assert.Equal(LineMode.Smooth, line.LineMode);
        Assert.Equal(MarkerShape.Circle, line.MarkerShape);
        Assert.Equal("#FF0000", line.Color);
        Assert.Equal(3, line.StrokeWidth);
        Assert.Equal(4, line.MarkerSize);
        Assert.Collection(
            line.Points,
            point =>
            {
                Assert.Equal(1, point.X);
                Assert.Equal(4, point.Y);
                Assert.Equal("middle point", point.Label);
            },
            point =>
            {
                Assert.Equal(2, point.X);
                Assert.Equal(3, point.Y);
            },
            point =>
            {
                Assert.Equal(3, point.X);
                Assert.Equal(2, point.Y);
            });

        var fill = Assert.Single(document.Series, item => item.Id == "fill-1");
        Assert.True(fill.FillArea);
        Assert.Equal("Region", fill.Name);
        Assert.Equal("#0080FF80", fill.Color);
        Assert.Equal(LineStyle.None, fill.LineStyle);
        Assert.Equal([3d, 2d, 1d], fill.Points.Select(point => point.X));

        var freePoints = Assert.Single(
            document.Series,
            item => item.Name.StartsWith("Points", StringComparison.Ordinal));
        Assert.Equal(MarkerShape.Cross, freePoints.MarkerShape);
        Assert.Equal("#0000FF", freePoints.Color);
        Assert.Equal(7, freePoints.MarkerSize);
        Assert.Equal(7, Assert.Single(freePoints.Points).X);

        var annotation = Assert.Single(document.Annotations);
        Assert.Equal("note", annotation.Id);
        Assert.Equal("Remember this", annotation.Text);
        Assert.Equal(6, annotation.X);
        Assert.Equal(7, annotation.Y);
        Assert.Equal(18, annotation.FontSize);
        Assert.Equal("#008000", annotation.Color);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("fills", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("groups", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("snapping", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("rich text", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void ImportZipWrapperReadsRootContentsXml()
    {
        var xml = MinimalDocument(
            """
            <vertex id="point" x="1.5" y="2.5" width="5" shape="diamond"/>
            """);
        var archive = CreateArchive(
            ("preview.pdf", [0x25, 0x50, 0x44, 0x46]),
            ("contents.xml", Utf8(xml)));

        using var nonSeekable = new NonSeekableReadStream(archive);
        var result = OGraphImporter.Import(nonSeekable, "zipped.ograph");

        Assert.Equal("zipped", result.Document.Title);
        var importedSeries = Assert.Single(result.Document.Series);
        var point = Assert.Single(importedSeries.Points);
        Assert.Equal(1.5, point.X);
        Assert.Equal(2.5, point.Y);
        Assert.Equal(MarkerShape.Diamond, importedSeries.MarkerShape);
        Assert.Equal(5, importedSeries.MarkerSize);
    }

    [Fact]
    public void ImportFitLineUsesStoredEndpointsAndLeavesSourceDataAsPoints()
    {
        var xml = MinimalDocument(
            """
            <vertex id="data-1" x="1" y="1" width="3" shape="circle">
              <color r="0" g="0" b="1"/>
            </vertex>
            <vertex id="endpoint-2" x="10" y="9" width="0" shape="none"/>
            <vertex id="data-2" x="2" y="3" width="3" shape="circle">
              <color r="0" g="0" b="1"/>
            </vertex>
            <vertex id="endpoint-1" x="0" y="-1" width="0" shape="none"/>
            <line id="fit" class="fit" method="linear-regression"
                  v1="endpoint-2" v2="endpoint-1" width="4" dash="dots">
              <color w="0.25"/>
              <data ids="data-2 data-1"/>
            </line>
            <label id="fit-name" owner="fit">
              <text><p><run><lit>Regression</lit></run></p></text>
            </label>
            """);

        var result = OGraphImporter.Import(Utf8(xml));

        var fit = Assert.Single(result.Document.Series, item => item.Id == "fit");
        Assert.Equal("Regression", fit.Name);
        Assert.Equal(LineStyle.Dotted, fit.LineStyle);
        Assert.Equal(LineMode.Straight, fit.LineMode);
        Assert.Equal("#404040", fit.Color);
        Assert.Collection(
            fit.Points,
            point =>
            {
                Assert.Equal(10, point.X);
                Assert.Equal(9, point.Y);
            },
            point =>
            {
                Assert.Equal(0, point.X);
                Assert.Equal(-1, point.Y);
            });

        var sourceData = Assert.Single(
            result.Document.Series,
            item => item.Name.StartsWith("Points", StringComparison.Ordinal));
        Assert.Equal([1d, 2d], sourceData.Points.Select(point => point.X));
        Assert.Equal(MarkerShape.Circle, sourceData.MarkerShape);
        Assert.Equal("#0000FF", sourceData.Color);
    }

    [Fact]
    public void ImportFreeVerticesGroupsByStyleWithoutReorderingPoints()
    {
        var xml = MinimalDocument(
            """
            <vertex id="a" x="3" y="1" width="4" shape="square">
              <color r="1" g="0" b="0"/>
            </vertex>
            <vertex id="c" x="8" y="2" width="2" shape="triangle">
              <color r="0" g="1" b="0"/>
            </vertex>
            <vertex id="b" x="1" y="5" width="4" shape="square">
              <color r="1" g="0" b="0"/>
            </vertex>
            """);

        var result = OGraphImporter.Import(Utf8(xml));

        Assert.Equal(2, result.Document.Series.Count);
        var squares = Assert.Single(
            result.Document.Series,
            item => item.MarkerShape == MarkerShape.Square);
        Assert.Equal([3d, 1d], squares.Points.Select(point => point.X));
        Assert.Equal("#FF0000", squares.Color);
        Assert.Equal(LineStyle.None, squares.LineStyle);

        var triangles = Assert.Single(
            result.Document.Series,
            item => item.MarkerShape == MarkerShape.Triangle);
        Assert.Equal(8, Assert.Single(triangles.Points).X);
        Assert.Equal("#00FF00", triangles.Color);
    }

    [Fact]
    public void ImportSymmetricErrorBarGeometryBecomesPointErrors()
    {
        var xml = MinimalDocument(
            """
            <vertex id="p1" x="1" y="5" width="4" shape="circle"/>
            <vertex id="p2" x="4" y="8" width="4" shape="circle"/>
            <vertex id="top" x="1" y="7" width="2" shape="tickmark"/>
            <vertex id="bottom" x="1" y="3" width="2" shape="tickmark"/>
            <vertex id="left" x="2.5" y="8" width="2" shape="tickmark"/>
            <vertex id="right" x="5.5" y="8" width="2" shape="tickmark"/>
            <line id="data" class="connect" method="straight">
              <vertices ids="p1 p2"/>
            </line>
            <line id="y-error" class="connect" method="straight" dash="solid">
              <vertices ids="top p1 bottom"/>
            </line>
            <line id="x-error" class="connect" method="straight" dash="solid">
              <vertices ids="left p2 right"/>
            </line>
            """);

        var result = OGraphImporter.Import(Utf8(xml));

        var importedSeries = Assert.Single(result.Document.Series);
        Assert.Equal("data", importedSeries.Id);
        Assert.Collection(
            importedSeries.Points,
            point =>
            {
                Assert.Equal(2, point.YError);
                Assert.Null(point.XError);
            },
            point =>
            {
                Assert.Equal(1.5, point.XError);
                Assert.Null(point.YError);
            });
        Assert.DoesNotContain(
            result.Document.Series,
            item => item.Id is "y-error" or "x-error");
    }

    [Fact]
    public void ImportDtdAndExternalEntityAreProhibited()
    {
        var xml = $$"""
            <?xml version="1.0"?>
            <!DOCTYPE document [
              <!ENTITY external SYSTEM "file:///etc/passwd">
            ]>
            <document xmlns="{{NamespaceV1}}">
              <graph>
                <canvas w="520" h="420"/>
                <axis id="ax" dimension="x" min="0" max="10"/>
                <axis id="ay" dimension="y" min="0" max="10"/>
                <label id="note" x="0" y="0">
                  <text><p><run><lit>&external;</lit></run></p></text>
                </label>
              </graph>
            </document>
            """;

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(Utf8(xml)));
    }

    [Theory]
    [InlineData("<document")]
    [InlineData("<document xmlns=\"urn:not-graphsketcher\"><graph/></document>")]
    [InlineData("<document xmlns=\"http://www.omnigroup.com/namespace/OmniGraphSketcher/v1\"/>")]
    public void ImportMalformedOrWrongDocumentIsRejected(string xml)
    {
        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(Utf8(xml)));
    }

    [Fact]
    public void ImportNonFiniteNumericValueIsRejected()
    {
        var xml = MinimalDocument(
            """
            <vertex id="bad" x="NaN" y="1"/>
            """);

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(Utf8(xml)));
    }

    [Fact]
    public void ImportZipWithoutContentsXmlIsRejected()
    {
        var archive = CreateArchive(("preview.pdf", [1, 2, 3]));

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(archive));
    }

    [Fact]
    public void ImportZipWithAmbiguousContentsXmlIsRejected()
    {
        var xml = Utf8(MinimalDocument(string.Empty));
        var archive = CreateArchive(
            ("contents.xml", xml),
            ("Contents.xml", xml));

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(archive));
    }

    [Fact]
    public void ImportZipEntryCountLimitIsEnforced()
    {
        var xml = Utf8(MinimalDocument(string.Empty));
        var archive = CreateArchive(
            ("contents.xml", xml),
            ("preview.pdf", [1]),
            ("metadata.plist", [2]));
        var limits = GenerousLimits() with
        {
            MaximumArchiveEntries = 2,
        };

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(archive, limits: limits));
    }

    [Fact]
    public void ImportZipTotalUncompressedSizeLimitIsEnforced()
    {
        var xml = Utf8(MinimalDocument(string.Empty));
        var archive = CreateArchive(
            ("contents.xml", xml),
            ("large-preview.pdf", new byte[512]));
        var limits = GenerousLimits() with
        {
            MaximumArchiveUncompressedBytes = xml.Length + 128,
        };

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(archive, limits: limits));
    }

    [Fact]
    public void ImportXmlSizeLimitsAreEnforcedForPlainAndArchivedXml()
    {
        var xml = Utf8(MinimalDocument(
            $"""
             <label id="large" x="1" y="1">
               <text><p><run><lit>{new string('x', 512)}</lit></run></p></text>
             </label>
             """));
        var limits = GenerousLimits() with
        {
            MaximumXmlBytes = 256,
        };

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(xml, limits: limits));

        var archive = CreateArchive(("contents.xml", xml));
        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(archive, limits: limits));
    }

    [Fact]
    public void ImportCompressedInputSizeLimitIsEnforced()
    {
        var xml = Utf8(MinimalDocument(string.Empty));
        var limits = GenerousLimits() with
        {
            MaximumInputBytes = xml.Length - 1,
        };

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(xml, limits: limits));
    }

    [Fact]
    public void ImportCorruptZipIsRejected()
    {
        byte[] corruptZip = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00];

        Assert.Throws<InvalidDataException>(
            () => OGraphImporter.Import(corruptZip));
    }

    private static OGraphImportLimits GenerousLimits() =>
        new()
        {
            MaximumArchiveEntries = 32,
            MaximumArchiveUncompressedBytes = 1_000_000,
            MaximumXmlBytes = 100_000,
            MaximumInputBytes = 1_000_000,
        };

    private static string MinimalDocument(string graphContent) =>
        $$"""
          <?xml version="1.0" encoding="utf-8"?>
          <document xmlns="{{NamespaceV1}}">
            <graph>
              <canvas w="520" h="420">
                <whitespace top="20" right="20" bottom="20" left="20"/>
              </canvas>
              <axis id="ax" dimension="x" min="0" max="10">
                <ticks spacing="1"/>
                <tick-labels visible="true"/>
              </axis>
              <axis id="ay" dimension="y" min="0" max="10">
                <ticks spacing="1"/>
                <tick-labels visible="true"/>
              </axis>
              {{graphContent}}
            </graph>
          </document>
          """;

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] CreateArchive(
        params (string Name, byte[] Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var (name, contents) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using var entryStream = entry.Open();
                entryStream.Write(contents);
            }
        }

        return stream.ToArray();
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
