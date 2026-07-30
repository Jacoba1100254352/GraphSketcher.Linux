using System.Globalization;
using System.Text;
using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

public enum DelimitedImportLayout
{
    Auto,
    CommonX,
    PairedXY,
}

/// <summary>
/// Controls conversion of CSV, TSV, or pasted spreadsheet text into graph series.
/// </summary>
public sealed class DelimitedImportOptions
{
    public char? Delimiter { get; set; }

    public bool? HasHeader { get; set; }

    public DelimitedImportLayout Layout { get; set; } = DelimitedImportLayout.Auto;

    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public bool ThrowOnInvalidRows { get; set; }
}

public sealed record DelimitedImportIssue(
    int Row,
    int? Column,
    string Message,
    string? Value = null);

public sealed class DelimitedImportResult
{
    internal DelimitedImportResult(
        IReadOnlyList<GraphSeries> series,
        IReadOnlyList<DelimitedImportIssue> issues,
        char delimiter,
        bool headerDetected,
        int rowsRead)
    {
        Series = series;
        Issues = issues;
        Delimiter = delimiter;
        HeaderDetected = headerDetected;
        RowsRead = rowsRead;
    }

    public IReadOnlyList<GraphSeries> Series { get; }

    public IReadOnlyList<DelimitedImportIssue> Issues { get; }

    public char Delimiter { get; }

    public bool HeaderDetected { get; }

    public int RowsRead { get; }
}

/// <summary>
/// Imports RFC-4180-style delimited data and spreadsheet clipboard text.
/// </summary>
public static class DelimitedDataImporter
{
    public const int MaximumInputCharacters = 16 * 1024 * 1024;
    public const int MaximumRowCount = GraphDocument.MaximumTotalPointCount + 1;
    public const int MaximumColumnCount = GraphDocument.MaximumSeriesCount * 2;
    public const int MaximumFieldCharacters = 16_384;
    public const int MaximumIssueCount = 10_000;

    private static readonly string[] Palette =
    [
        "#2563EB",
        "#DC2626",
        "#059669",
        "#7C3AED",
        "#D97706",
        "#0891B2",
        "#DB2777",
        "#4B5563",
    ];

    public static DelimitedImportResult Import(
        string text,
        DelimitedImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumInputCharacters)
        {
            throw new FormatException(
                $"Imported text cannot exceed {MaximumInputCharacters:N0} characters.");
        }

        options ??= new DelimitedImportOptions();
        ValidateOptions(options);

        var delimiter = options.Delimiter ?? DetectDelimiter(text);
        var parsedRows = ParseRows(text, delimiter)
            .Where(row => !row.IsBlank)
            .ToArray();

        if (parsedRows.Length == 0)
        {
            throw new FormatException("The imported text contains no data rows.");
        }

        var headerDetected = options.HasHeader ?? LooksLikeHeader(parsedRows, options.Culture);
        var header = headerDetected ? parsedRows[0].Fields : null;
        var dataRows = parsedRows.AsSpan(headerDetected ? 1 : 0);
        if (dataRows.Length == 0)
        {
            throw new FormatException("The imported text contains a header but no data rows.");
        }

        var layout = options.Layout == DelimitedImportLayout.Auto
            ? DelimitedImportLayout.CommonX
            : options.Layout;
        var issues = new List<DelimitedImportIssue>();

        var series = layout switch
        {
            DelimitedImportLayout.CommonX =>
                ImportCommonX(dataRows, header, options.Culture, issues),
            DelimitedImportLayout.PairedXY =>
                ImportPairedXY(dataRows, header, options.Culture, issues),
            _ => throw new InvalidOperationException("The import layout is not supported."),
        };

        if (options.ThrowOnInvalidRows && issues.Count > 0)
        {
            var first = issues[0];
            throw new FormatException(
                $"Import failed at row {first.Row}" +
                (first.Column is { } column ? $", column {column}" : string.Empty) +
                $": {first.Message}");
        }

        if (series.Count == 0 || series.All(item => item.Points.Count == 0))
        {
            throw new FormatException("The imported text contains no usable numeric data.");
        }

        return new DelimitedImportResult(
            series.AsReadOnly(),
            issues.AsReadOnly(),
            delimiter,
            headerDetected,
            dataRows.Length);
    }

    public static IReadOnlyList<GraphSeries> ImportSeries(
        string text,
        DelimitedImportOptions? options = null) =>
        Import(text, options).Series;

    private static List<GraphSeries> ImportCommonX(
        ReadOnlySpan<ParsedRow> rows,
        string[]? header,
        CultureInfo culture,
        List<DelimitedImportIssue> issues)
    {
        var columnCount = header is { Length: > 0 }
            ? header.Length
            : rows.ToArray().Max(row => row.Fields.Length);
        var singleColumn = columnCount == 1;
        var firstYColumn = singleColumn ? 0 : 1;
        if (columnCount - firstYColumn > GraphDocument.MaximumSeriesCount)
        {
            throw new FormatException(
                $"Imported data cannot contain more than " +
                $"{GraphDocument.MaximumSeriesCount:N0} series.");
        }

        var series = CreateSeries(header, firstYColumn, columnCount);

        var sequentialX = 0;
        var totalPointCount = 0;
        foreach (var row in rows)
        {
            sequentialX++;
            if (row.Fields.Length != columnCount)
            {
                AddIssue(issues, new DelimitedImportIssue(
                    row.SourceLine,
                    null,
                    $"Expected {columnCount} columns but found {row.Fields.Length}."));
            }

            double x;
            if (singleColumn)
            {
                x = sequentialX;
            }
            else if (!TryReadNumber(row, 0, culture, issues, required: true, out x))
            {
                continue;
            }

            for (var column = firstYColumn; column < columnCount; column++)
            {
                if (!TryReadNumber(row, column, culture, issues, required: false, out var y))
                {
                    continue;
                }

                EnsurePointCapacity(ref totalPointCount);
                series[column - firstYColumn].Points.Add(new DataPoint(x, y));
            }
        }

        return series;
    }

    private static List<GraphSeries> ImportPairedXY(
        ReadOnlySpan<ParsedRow> rows,
        string[]? header,
        CultureInfo culture,
        List<DelimitedImportIssue> issues)
    {
        var columnCount = header is { Length: > 0 }
            ? header.Length
            : rows.ToArray().Max(row => row.Fields.Length);
        var pairCount = columnCount / 2;
        if (pairCount == 0)
        {
            throw new FormatException("Paired X/Y import requires at least two columns.");
        }

        if (columnCount % 2 != 0)
        {
            AddIssue(issues, new DelimitedImportIssue(
                rows[0].SourceLine,
                columnCount,
                "The final unpaired column was ignored."));
        }

        var series = new List<GraphSeries>(pairCount);
        for (var pair = 0; pair < pairCount; pair++)
        {
            var yColumn = (pair * 2) + 1;
            series.Add(CreateSeries(
                GetSeriesName(header, yColumn, pair + 1),
                pair));
        }

        var totalPointCount = 0;
        foreach (var row in rows)
        {
            if (row.Fields.Length != columnCount)
            {
                AddIssue(issues, new DelimitedImportIssue(
                    row.SourceLine,
                    null,
                    $"Expected {columnCount} columns but found {row.Fields.Length}."));
            }

            for (var pair = 0; pair < pairCount; pair++)
            {
                var xColumn = pair * 2;
                var yColumn = xColumn + 1;
                var hasX = TryReadNumber(row, xColumn, culture, issues, required: false, out var x);
                var hasY = TryReadNumber(row, yColumn, culture, issues, required: false, out var y);

                if (hasX != hasY)
                {
                    AddIssue(issues, new DelimitedImportIssue(
                        row.SourceLine,
                        hasX ? yColumn + 1 : xColumn + 1,
                        "Both values in an X/Y pair are required."));
                    continue;
                }

                if (hasX)
                {
                    EnsurePointCapacity(ref totalPointCount);
                    series[pair].Points.Add(new DataPoint(x, y));
                }
            }
        }

        return series;
    }

    private static List<GraphSeries> CreateSeries(
        string[]? header,
        int firstYColumn,
        int columnCount)
    {
        var series = new List<GraphSeries>(columnCount - firstYColumn);
        for (var column = firstYColumn; column < columnCount; column++)
        {
            series.Add(CreateSeries(
                GetSeriesName(header, column, (column - firstYColumn) + 1),
                column - firstYColumn));
        }

        return series;
    }

    private static GraphSeries CreateSeries(string name, int index) =>
        new()
        {
            Name = name,
            Color = Palette[index % Palette.Length],
        };

    private static string GetSeriesName(string[]? header, int column, int ordinal)
    {
        if (header is not null &&
            column < header.Length &&
            !string.IsNullOrWhiteSpace(header[column]))
        {
            var name = header[column].Trim();
            return name.Length <= 512 ? name : name[..512];
        }

        return $"Series {ordinal}";
    }

    private static bool TryReadNumber(
        ParsedRow row,
        int zeroBasedColumn,
        CultureInfo culture,
        List<DelimitedImportIssue> issues,
        bool required,
        out double value)
    {
        value = default;
        if (zeroBasedColumn >= row.Fields.Length ||
            string.IsNullOrWhiteSpace(row.Fields[zeroBasedColumn]))
        {
            if (required)
            {
                AddIssue(issues, new DelimitedImportIssue(
                    row.SourceLine,
                    zeroBasedColumn + 1,
                    "A numeric value is required."));
            }

            return false;
        }

        var text = row.Fields[zeroBasedColumn].Trim();
        if (double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                culture,
                out value) &&
            double.IsFinite(value))
        {
            return true;
        }

        AddIssue(issues, new DelimitedImportIssue(
            row.SourceLine,
            zeroBasedColumn + 1,
            "The value is not a finite number.",
            text));
        return false;
    }

    private static bool LooksLikeHeader(
        IReadOnlyList<ParsedRow> rows,
        CultureInfo culture)
    {
        var first = rows[0].Fields;
        var firstHasText = first.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            !IsNumber(value, culture));
        if (!firstHasText || rows.Count == 1)
        {
            return false;
        }

        return rows
            .Skip(1)
            .Take(5)
            .Any(row => row.Fields.Any(value => IsNumber(value, culture)));
    }

    private static bool IsNumber(string value, CultureInfo culture) =>
        double.TryParse(
            value.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            culture,
            out var number) &&
        double.IsFinite(number);

    private static char DetectDelimiter(string text)
    {
        ReadOnlySpan<char> candidates = ['\t', ',', ';', '|'];
        var counts = new int[candidates.Length];
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && current is '\r' or '\n')
            {
                if (counts.Any(count => count > 0))
                {
                    break;
                }

                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            for (var candidate = 0; candidate < candidates.Length; candidate++)
            {
                if (current == candidates[candidate])
                {
                    counts[candidate]++;
                }
            }
        }

        var bestIndex = 0;
        for (var index = 1; index < counts.Length; index++)
        {
            if (counts[index] > counts[bestIndex])
            {
                bestIndex = index;
            }
        }

        return counts[bestIndex] == 0 ? ',' : candidates[bestIndex];
    }

    private static List<ParsedRow> ParseRows(string text, char delimiter)
    {
        var rows = new List<ParsedRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var sourceLine = 1;
        var rowStartLine = 1;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (inQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        AppendFieldCharacter(field, '"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    AppendFieldCharacter(field, current);
                    if (current == '\n' || current == '\r' &&
                        (index + 1 >= text.Length || text[index + 1] != '\n'))
                    {
                        sourceLine++;
                    }
                }

                continue;
            }

            if (current == '"' && IsOnlyWhitespace(field))
            {
                field.Clear();
                inQuotes = true;
            }
            else if (current == delimiter)
            {
                AddField(fields, field);
                field.Clear();
            }
            else if (current is '\r' or '\n')
            {
                AddField(fields, field);
                field.Clear();
                AddRow(rows, fields, rowStartLine);
                fields.Clear();

                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                sourceLine++;
                rowStartLine = sourceLine;
            }
            else
            {
                AppendFieldCharacter(field, current);
            }
        }

        if (inQuotes)
        {
            throw new FormatException($"A quoted field beginning on row {rowStartLine} is not terminated.");
        }

        if (field.Length > 0 || fields.Count > 0 || text[^1] == delimiter)
        {
            AddField(fields, field);
            AddRow(rows, fields, rowStartLine);
        }

        return rows;
    }

    private static void AppendFieldCharacter(StringBuilder field, char value)
    {
        if (field.Length >= MaximumFieldCharacters)
        {
            throw new FormatException(
                $"Imported fields cannot exceed {MaximumFieldCharacters:N0} characters.");
        }

        field.Append(value);
    }

    private static void AddField(List<string> fields, StringBuilder field)
    {
        if (fields.Count >= MaximumColumnCount)
        {
            throw new FormatException(
                $"Imported rows cannot contain more than {MaximumColumnCount:N0} columns.");
        }

        fields.Add(field.ToString());
    }

    private static void AddRow(
        List<ParsedRow> rows,
        List<string> fields,
        int sourceLine)
    {
        if (rows.Count >= MaximumRowCount)
        {
            throw new FormatException(
                $"Imported text cannot contain more than {MaximumRowCount:N0} rows.");
        }

        rows.Add(new ParsedRow(fields.ToArray(), sourceLine));
    }

    private static void EnsurePointCapacity(ref int totalPointCount)
    {
        if (totalPointCount >= GraphDocument.MaximumTotalPointCount)
        {
            throw new FormatException(
                $"Imported data cannot contain more than " +
                $"{GraphDocument.MaximumTotalPointCount:N0} total points.");
        }

        totalPointCount++;
    }

    private static void AddIssue(
        List<DelimitedImportIssue> issues,
        DelimitedImportIssue issue)
    {
        if (issues.Count >= MaximumIssueCount)
        {
            throw new FormatException(
                $"Imported data contains more than {MaximumIssueCount:N0} invalid cells or rows.");
        }

        issues.Add(issue);
    }

    private static bool IsOnlyWhitespace(StringBuilder value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateOptions(DelimitedImportOptions options)
    {
        if (options.Delimiter is '\r' or '\n' or '"')
        {
            throw new ArgumentException(
                "The delimiter cannot be a quote or line ending.",
                nameof(options));
        }

        if (!Enum.IsDefined(options.Layout))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The import layout is not recognized.");
        }

        ArgumentNullException.ThrowIfNull(options.Culture);
    }

    private sealed record ParsedRow(string[] Fields, int SourceLine)
    {
        public bool IsBlank => Fields.All(string.IsNullOrWhiteSpace);
    }
}
