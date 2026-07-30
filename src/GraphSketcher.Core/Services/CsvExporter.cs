using System.Globalization;
using System.Text;
using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

/// <summary>
/// Exports graph data as UTF-8 CSV while neutralizing spreadsheet formulas in
/// user-controlled text cells.
/// </summary>
public static class CsvExporter
{
    public static async Task ExportAsync(
        GraphDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The destination stream must be writable.",
                nameof(destination));
        }

        document.EnsureValid();
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 16_384,
            leaveOpen: true);

        await writer
            .WriteLineAsync(
                "series,x,y,x_error,y_error,label".AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var series in document.Series)
        {
            foreach (var point in series.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = string.Join(
                    ",",
                    EscapeTextCell(series.Name),
                    point.X.ToString("G17", CultureInfo.InvariantCulture),
                    point.Y.ToString("G17", CultureInfo.InvariantCulture),
                    point.XError?.ToString("G17", CultureInfo.InvariantCulture) ??
                    string.Empty,
                    point.YError?.ToString("G17", CultureInfo.InvariantCulture) ??
                    string.Empty,
                    EscapeTextCell(point.Label ?? string.Empty));
                await writer
                    .WriteLineAsync(line.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static string EscapeTextCell(string value)
    {
        var safeValue = IsSpreadsheetFormula(value) ? $"'{value}" : value;
        return safeValue.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? safeValue
            : $"\"{safeValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsSpreadsheetFormula(string value)
    {
        var index = 0;
        while (index < value.Length && value[index] == ' ')
        {
            index++;
        }

        return index < value.Length &&
               value[index] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';
    }
}
