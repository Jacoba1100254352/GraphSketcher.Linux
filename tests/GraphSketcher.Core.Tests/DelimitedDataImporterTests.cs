using System.Globalization;
using System.Text;
using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class DelimitedDataImporterTests
{
    [Fact]
    public void ImportsQuotedCsvHeadersAndMultipleSeries()
    {
        const string text =
            "X,\"Revenue, net\",\"Forecast \"\"official\"\"\"\n" +
            "1,2.5,3\n" +
            "2,4.5,5\n";

        var result = DelimitedDataImporter.Import(text);

        Assert.Equal(',', result.Delimiter);
        Assert.True(result.HeaderDetected);
        Assert.Equal(2, result.Series.Count);
        Assert.Equal("Revenue, net", result.Series[0].Name);
        Assert.Equal("Forecast \"official\"", result.Series[1].Name);
        Assert.Equal(2, result.Series[0].Points.Count);
        Assert.Equal(4.5, result.Series[0].Points[1].Y);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DetectsSpreadsheetTabSeparatedData()
    {
        const string text = "1\t10\t100\r\n2\t20\t200\r\n";

        var result = DelimitedDataImporter.Import(text);

        Assert.Equal('\t', result.Delimiter);
        Assert.False(result.HeaderDetected);
        Assert.Equal(2, result.Series.Count);
        Assert.Equal(20, result.Series[0].Points[1].Y);
        Assert.Equal(200, result.Series[1].Points[1].Y);
    }

    [Fact]
    public void ImportsSingleColumnUsingOneBasedRowNumbers()
    {
        const string text = "Temperature\n12\n15\n18\n";

        var result = DelimitedDataImporter.Import(text);

        var series = Assert.Single(result.Series);
        Assert.Equal("Temperature", series.Name);
        Assert.Equal([1d, 2d, 3d], series.Points.Select(point => point.X));
        Assert.Equal([12d, 15d, 18d], series.Points.Select(point => point.Y));
    }

    [Fact]
    public void ImportsPairedXYColumns()
    {
        const string text = "Time A,Observed,Time B,Predicted\n0,1,10,3\n1,2,20,4\n";
        var options = new DelimitedImportOptions
        {
            Layout = DelimitedImportLayout.PairedXY,
            HasHeader = true,
        };

        var result = DelimitedDataImporter.Import(text, options);

        Assert.Equal(2, result.Series.Count);
        Assert.Equal("Observed", result.Series[0].Name);
        Assert.Equal("Predicted", result.Series[1].Name);
        Assert.Equal(20, result.Series[1].Points[1].X);
        Assert.Equal(4, result.Series[1].Points[1].Y);
    }

    [Fact]
    public void ReportsBadCellsAndContinuesWithUsableRows()
    {
        const string text = "X,A,B\n1,2,3\n2,nope,4\nbad,5,6\n3,,9\n";

        var result = DelimitedDataImporter.Import(text);

        Assert.Single(result.Series[0].Points);
        Assert.Equal(3, result.Series[1].Points.Count);
        Assert.Equal(2, result.Issues.Count);
        Assert.Contains(result.Issues, issue => issue.Value == "nope");
        Assert.Contains(result.Issues, issue => issue.Value == "bad");
    }

    [Fact]
    public void StrictModeRejectsFirstInvalidCell()
    {
        var options = new DelimitedImportOptions { ThrowOnInvalidRows = true };

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import("X,Y\n1,nope\n", options));

        Assert.Contains("row 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportsCultureSpecificNumbersWithExplicitDelimiter()
    {
        var options = new DelimitedImportOptions
        {
            Delimiter = ';',
            HasHeader = true,
            Culture = CultureInfo.GetCultureInfo("de-DE"),
        };

        var result = DelimitedDataImporter.Import("X;Y\n1,5;2,5\n2,5;3,5\n", options);

        Assert.Equal(1.5, result.Series[0].Points[0].X);
        Assert.Equal(2.5, result.Series[0].Points[0].Y);
    }

    [Fact]
    public void SupportsNewlinesInsideQuotedHeaders()
    {
        const string text = "X,\"Long\r\nname\"\r\n1,2\r\n";

        var result = DelimitedDataImporter.Import(text);

        Assert.Equal("Long\r\nname", result.Series[0].Name);
        Assert.Equal(1, result.RowsRead);
    }

    [Fact]
    public void HeaderDefinesExpectedColumnsAndExtraCellsAreReported()
    {
        var result = DelimitedDataImporter.Import("X,Y\n1,2,unexpected\n2,3\n");

        Assert.Single(result.Series);
        Assert.Equal(2, result.Series[0].Points.Count);
        Assert.Single(result.Issues);
        Assert.Contains("Expected 2 columns", result.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnterminatedQuotedField()
    {
        Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import("X,Y\n1,\"unfinished"));
    }

    [Fact]
    public void RejectsTextWithoutNumericData()
    {
        Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import("X,Y\nhello,world\n"));
    }

    [Fact]
    public void RejectsInputBeyondCharacterLimitBeforeParsing()
    {
        var text = new string(
            '1',
            DelimitedDataImporter.MaximumInputCharacters + 1);

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text));

        Assert.Contains("characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTooManyRows()
    {
        var text = new StringBuilder(
            (DelimitedDataImporter.MaximumRowCount + 1) * 2);
        for (var row = 0; row <= DelimitedDataImporter.MaximumRowCount; row++)
        {
            text.Append("1\n");
        }

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text.ToString()));

        Assert.Contains("rows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTooManyColumns()
    {
        var text = string.Join(
            ',',
            Enumerable.Repeat("1", DelimitedDataImporter.MaximumColumnCount + 1));

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text));

        Assert.Contains("columns", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTooManyCommonXSeries()
    {
        var text = string.Join(
            ',',
            Enumerable.Repeat("1", GraphDocument.MaximumSeriesCount + 2));

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text));

        Assert.Contains("series", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOversizedFields()
    {
        var text = new string(
            '1',
            DelimitedDataImporter.MaximumFieldCharacters + 1);

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text));

        Assert.Contains("fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsExcessiveInvalidCellReports()
    {
        var text = new StringBuilder("X,Y\n");
        for (var row = 0; row <= DelimitedDataImporter.MaximumIssueCount; row++)
        {
            text.Append("bad,1\n");
        }

        var exception = Assert.Throws<FormatException>(
            () => DelimitedDataImporter.Import(text.ToString()));

        Assert.Contains("invalid cells or rows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatesHeaderNamesToTheDocumentLimit()
    {
        var header = new string('A', 600);
        var result = DelimitedDataImporter.Import($"X,{header}\n1,2\n");

        var series = Assert.Single(result.Series);
        Assert.Equal(512, series.Name.Length);
    }
}
