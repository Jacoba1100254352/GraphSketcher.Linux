using System.Text;
using System.Text.Json;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class DocumentSerializerTests
{
    [Fact]
    public void RoundTripPreservesCompleteDocument()
    {
        var original = TestDocumentFactory.Create();

        var json = DocumentSerializer.Serialize(original);
        var restored = DocumentSerializer.Deserialize(json);

        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(LegendPosition.BottomLeft, restored.Canvas.LegendPosition);
        Assert.Equal(1, restored.XAxis.TickSpacing);
        Assert.True(restored.Series[0].FillArea);
        Assert.Equal(LineMode.Smooth, restored.Series[0].LineMode);
        Assert.Equal(0.1, restored.Series[0].Points[0].XError);
        Assert.Equal(0.2, restored.Series[0].Points[0].YError);
        Assert.Equal(AnnotationKind.Line, restored.Annotations[0].Kind);
        Assert.Empty(restored.Validate());
    }

    [Fact]
    public void SerializationUsesReadableCamelCaseEnums()
    {
        var json = DocumentSerializer.Serialize(TestDocumentFactory.Create());

        Assert.Contains("\"lineStyle\": \"dashed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"legendPosition\": \"bottomLeft\"", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeAcceptsCommentsTrailingCommasAndCaseInsensitiveNames()
    {
        const string json = """
            {
              // Compatible hand-authored file
              "TITLE": "Example",
              "series": [],
              "annotations": [],
            }
            """;

        var document = DocumentSerializer.Deserialize(json);

        Assert.Equal("Example", document.Title);
    }

    [Fact]
    public void DeserializeRejectsInvalidDocuments()
    {
        const string json = """
            {
              "title": "Broken",
              "canvas": null,
              "xAxis": {},
              "yAxis": {},
              "series": [],
              "annotations": []
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentSerializer.Deserialize(json));

        Assert.Contains("canvas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadTranslatesMalformedJsonToInvalidDataException()
    {
        const string json = """{"series":[""";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => DocumentSerializer.LoadAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeTranslatesUnknownEnumToInvalidDataException()
    {
        const string json = """
            {
              "series": [
                {
                  "lineStyle": "scribbled",
                  "points": []
                }
              ],
              "annotations": []
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentSerializer.Deserialize(json));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Contains("unsupported value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadRejectsOversizedSeekableStreamBeforeReading()
    {
        await using var stream = new DeclaredLengthStream(
            DocumentSerializer.MaximumDocumentBytes + 1L);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => DocumentSerializer.LoadAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.False(stream.WasRead);
        Assert.Contains("64 MiB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsTooManySeriesBeforeTypedAllocation()
    {
        var json = CreateArrayDocument(
            "SERIES",
            GraphDocument.MaximumSeriesCount + 1,
            "{}");

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentSerializer.Deserialize(json));

        Assert.Contains("256 series", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsTooManyTotalPointsBeforeTypedAllocation()
    {
        var points = CreateArray(
            GraphDocument.MaximumTotalPointCount + 1,
            "null");
        var json = $$"""{"series":[{"points":[{{points}}]}],"annotations":[]}""";

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentSerializer.Deserialize(json));

        Assert.Contains("250,000 total points", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsTooManyAnnotationsBeforeTypedAllocation()
    {
        var json = CreateArrayDocument(
            "annotations",
            GraphDocument.MaximumAnnotationCount + 1,
            "{}");

        var exception = Assert.Throws<InvalidDataException>(
            () => DocumentSerializer.Deserialize(json));

        Assert.Contains("10,000 annotations", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CloneIsDeepAndIndependent()
    {
        var original = TestDocumentFactory.Create();

        var clone = DocumentSerializer.Clone(original);
        clone.Series[0].Points[0].Y = 999;

        Assert.Equal(2, original.Series[0].Points[0].Y);
        Assert.Equal(999, clone.Series[0].Points[0].Y);
    }

    [Fact]
    public async Task AsyncStreamRoundTripLeavesStreamUsable()
    {
        var original = TestDocumentFactory.Create();
        using var stream = new MemoryStream();

        await DocumentSerializer.SaveAsync(
            original,
            stream,
            TestContext.Current.CancellationToken);
        Assert.True(stream.CanWrite);
        stream.Position = 0;
        var restored = await DocumentSerializer.LoadAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(original.Title, restored.Title);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Utf8SerializationHasNoByteOrderMark()
    {
        var bytes = DocumentSerializer.SerializeToUtf8(TestDocumentFactory.Create());

        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'{', bytes[0]);
        Assert.DoesNotContain(Encoding.UTF8.Preamble, bytes);
    }

    [Fact]
    public void PublicFileExtensionMatchesFileAssociation()
    {
        Assert.Equal(".graphsketch", DocumentSerializer.DefaultFileExtension);
    }

    [Fact]
    public async Task GettingStartedSampleLoadsWithinResourceLimits()
    {
        var samplePath = Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "Getting Started.graphsketch");

        var document = await DocumentSerializer.LoadAsync(
            samplePath,
            TestContext.Current.CancellationToken);

        Assert.Equal("Cooling experiment", document.Title);
        Assert.NotEmpty(document.Series);
        Assert.Empty(document.Validate());
    }

    private static string CreateArrayDocument(
        string propertyName,
        int itemCount,
        string item) =>
        $$"""{"{{propertyName}}":[{{CreateArray(itemCount, item)}}]}""";

    private static string CreateArray(int itemCount, string item)
    {
        var builder = new StringBuilder((item.Length + 1) * itemCount);
        for (var index = 0; index < itemCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(item);
        }

        return builder.ToString();
    }

    private sealed class DeclaredLengthStream(long length) : Stream
    {
        public bool WasRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("The oversized stream should not be read.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            throw new InvalidOperationException("The oversized stream should not be read.");
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
