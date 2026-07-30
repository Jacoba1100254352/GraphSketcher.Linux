using System.Text;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public async Task ExportUsesUtf8AndLeavesDestinationOpen()
    {
        using var stream = new MemoryStream();

        await CsvExporter.ExportAsync(
            TestDocumentFactory.Create(),
            stream,
            TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
        var bytes = stream.ToArray();
        Assert.NotEmpty(bytes);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.StartsWith(
            "series,x,y,x_error,y_error,label\n",
            Encoding.UTF8.GetString(bytes),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportNeutralizesSpreadsheetFormulasInTextCells()
    {
        var document = new GraphDocument
        {
            Series =
            [
                new GraphSeries
                {
                    Name = "=HYPERLINK(\"https://example.invalid\",\"open\")",
                    Points =
                    [
                        new DataPoint(1, 2) { Label = "  @malicious" },
                    ],
                },
            ],
        };
        using var stream = new MemoryStream();

        await CsvExporter.ExportAsync(
            document,
            stream,
            TestContext.Current.CancellationToken);
        var csv = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains(
            "\"'=HYPERLINK(\"\"https://example.invalid\"\",\"\"open\"\")\"",
            csv,
            StringComparison.Ordinal);
        Assert.Contains("'  @malicious", csv, StringComparison.Ordinal);
    }
}
