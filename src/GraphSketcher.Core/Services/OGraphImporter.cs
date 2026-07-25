using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

/// <summary>
/// Resource limits applied while reading an untrusted legacy document.
/// </summary>
public sealed record OGraphImportLimits
{
    public const int DefaultMaximumArchiveEntries = 128;
    public const long DefaultMaximumArchiveUncompressedBytes = 32L * 1024 * 1024;
    public const long DefaultMaximumXmlBytes = 16L * 1024 * 1024;
    public const long DefaultMaximumInputBytes = 64L * 1024 * 1024;

    public int MaximumArchiveEntries { get; init; } = DefaultMaximumArchiveEntries;

    public long MaximumArchiveUncompressedBytes { get; init; } =
        DefaultMaximumArchiveUncompressedBytes;

    public long MaximumXmlBytes { get; init; } = DefaultMaximumXmlBytes;

    public long MaximumInputBytes { get; init; } = DefaultMaximumInputBytes;

    internal void EnsureValid()
    {
        if (MaximumArchiveEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumArchiveEntries),
                "The maximum archive entry count must be positive.");
        }

        EnsurePositiveBufferLimit(
            MaximumArchiveUncompressedBytes,
            nameof(MaximumArchiveUncompressedBytes));
        EnsurePositiveBufferLimit(MaximumXmlBytes, nameof(MaximumXmlBytes));
        EnsurePositiveBufferLimit(MaximumInputBytes, nameof(MaximumInputBytes));
    }

    private static void EnsurePositiveBufferLimit(long value, string parameterName)
    {
        if (value is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Import byte limits must be positive and no greater than Int32.MaxValue.");
        }
    }
}

/// <summary>
/// A successfully imported graph and any compatibility losses encountered.
/// </summary>
/// <param name="Document">The portable graph document.</param>
/// <param name="Warnings">Human-readable compatibility warnings.</param>
public sealed record OGraphImportResult(
    GraphDocument Document,
    IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>
/// Safely reads original GraphSketcher plain-XML and ZIP-wrapped .ograph files.
/// </summary>
public static class OGraphImporter
{
    public const string NamespaceV1 =
        "http://www.omnigroup.com/namespace/OmniGraphSketcher/v1";

    private const int MaximumWarningCount = 256;
    private static readonly XNamespace LegacyNamespace = NamespaceV1;

    /// <summary>
    /// Imports a legacy document from disk without modifying the source file.
    /// </summary>
    public static OGraphImportResult Import(
        string filePath,
        OGraphImportLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return Import(stream, Path.GetFileName(filePath), limits);
    }

    /// <summary>
    /// Imports a legacy document from a byte array.
    /// </summary>
    public static OGraphImportResult Import(
        byte[] data,
        string? sourceName = null,
        OGraphImportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream(data, writable: false);
        return Import(stream, sourceName, limits);
    }

    /// <summary>
    /// Imports a legacy document from the current position of a stream.
    /// The caller retains ownership of the stream.
    /// </summary>
    public static OGraphImportResult Import(
        Stream input,
        string? sourceName = null,
        OGraphImportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead)
        {
            throw new ArgumentException("The input stream must be readable.", nameof(input));
        }

        limits ??= new OGraphImportLimits();
        limits.EnsureValid();

        var inputBytes = ReadWithLimit(
            input,
            limits.MaximumInputBytes,
            "The .ograph input exceeds the compressed input size limit.");

        byte[] xmlBytes;
        if (HasZipSignature(inputBytes))
        {
            xmlBytes = ReadXmlFromArchive(inputBytes, limits);
        }
        else
        {
            if (inputBytes.LongLength > limits.MaximumXmlBytes)
            {
                throw new InvalidDataException(
                    "The .ograph XML exceeds the XML size limit.");
            }

            xmlBytes = inputBytes;
        }

        var xml = LoadXml(xmlBytes, limits.MaximumXmlBytes);
        return ConvertDocument(xml, sourceName);
    }

    private static byte[] ReadXmlFromArchive(
        byte[] archiveBytes,
        OGraphImportLimits limits)
    {
        try
        {
            using var archiveStream = new MemoryStream(archiveBytes, writable: false);
            using var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Read,
                leaveOpen: false);

            if (archive.Entries.Count > limits.MaximumArchiveEntries)
            {
                throw new InvalidDataException(
                    $"The .ograph archive contains more than " +
                    $"{limits.MaximumArchiveEntries.ToString(CultureInfo.InvariantCulture)} entries.");
            }

            long totalLength = 0;
            var contentEntries = new List<ZipArchiveEntry>();

            foreach (var entry in archive.Entries)
            {
                try
                {
                    totalLength = checked(totalLength + entry.Length);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException(
                        "The .ograph archive reports an invalid uncompressed size.",
                        exception);
                }

                if (totalLength > limits.MaximumArchiveUncompressedBytes)
                {
                    throw new InvalidDataException(
                        "The .ograph archive exceeds the total uncompressed size limit.");
                }

                if (string.Equals(
                        entry.FullName,
                        "contents.xml",
                        StringComparison.OrdinalIgnoreCase))
                {
                    contentEntries.Add(entry);
                }
            }

            if (contentEntries.Count == 0)
            {
                throw new InvalidDataException(
                    "The .ograph archive does not contain a root contents.xml entry.");
            }

            if (contentEntries.Count > 1)
            {
                throw new InvalidDataException(
                    "The .ograph archive contains more than one contents.xml entry.");
            }

            var contents = contentEntries[0];
            if (contents.Length > limits.MaximumXmlBytes)
            {
                throw new InvalidDataException(
                    "The archived contents.xml exceeds the XML size limit.");
            }

            using var contentsStream = contents.Open();
            return ReadWithLimit(
                contentsStream,
                limits.MaximumXmlBytes,
                "The archived contents.xml exceeds the XML size limit.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException)
        {
            throw new InvalidDataException(
                "The .ograph ZIP wrapper could not be read.",
                exception);
        }
    }

    private static XDocument LoadXml(byte[] xmlBytes, long maximumXmlBytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = maximumXmlBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try
        {
            using var stream = new MemoryStream(xmlBytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException(
                "The .ograph XML is malformed or contains prohibited XML features.",
                exception);
        }
    }

    private static OGraphImportResult ConvertDocument(
        XDocument xml,
        string? sourceName)
    {
        var root = xml.Root;
        if (root is null || root.Name != LegacyNamespace + "document")
        {
            throw new InvalidDataException(
                $"The XML root must be document in the {NamespaceV1} namespace.");
        }

        var graphElements = root.Elements(LegacyNamespace + "graph").ToList();
        if (graphElements.Count == 0)
        {
            throw new InvalidDataException("The .ograph document does not contain a graph.");
        }

        var warnings = new WarningCollector();
        if (graphElements.Count > 1)
        {
            warnings.AddOnce(
                "multiple-graphs",
                "The document contains multiple graphs; only the first graph was imported.");
        }

        var graph = graphElements[0];
        EnsureUniqueLegacyIds(graph);

        var labels = ParseLabels(graph, warnings);
        var axisLabelIds = new HashSet<string>(StringComparer.Ordinal);

        var xAxis = ParseAxis(
            graph,
            "x",
            labels,
            axisLabelIds,
            warnings);
        var yAxis = ParseAxis(
            graph,
            "y",
            labels,
            axisLabelIds,
            warnings);

        var vertices = ParseVertices(graph, labels, warnings);
        var vertexById = vertices.ToDictionary(
            vertex => vertex.Id,
            StringComparer.Ordinal);

        var lineElements = graph.Elements(LegacyNamespace + "line").ToList();
        var errorBars = RecognizeErrorBars(lineElements, vertexById, warnings);

        var series = new List<GraphSeries>();
        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        var consumedVertices = new HashSet<string>(
            errorBars.EndpointVertexIds,
            StringComparer.Ordinal);

        ParseLines(
            lineElements,
            vertexById,
            labels,
            errorBars,
            consumedVertices,
            modelIds,
            series,
            warnings);
        ParseFills(
            graph,
            vertexById,
            labels,
            errorBars,
            consumedVertices,
            modelIds,
            series,
            warnings);
        ParseFreeVertices(
            vertices,
            errorBars,
            consumedVertices,
            modelIds,
            series);

        var annotations = ParseAnnotations(
            labels,
            axisLabelIds,
            modelIds,
            warnings);

        if (graph.Elements(LegacyNamespace + "group").Any())
        {
            warnings.AddOnce(
                "groups",
                "Legacy groups were flattened; group relationships are not editable.");
        }

        var document = new GraphDocument
        {
            Title = GetDocumentTitle(sourceName),
            Canvas = ParseCanvas(graph, warnings),
            XAxis = xAxis,
            YAxis = yAxis,
            Series = series,
            Annotations = annotations,
        };

        try
        {
            document.EnsureValid();
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                "The imported .ograph data cannot be represented as a valid graph document.",
                exception);
        }

        return new OGraphImportResult(document, warnings.ToArray());
    }

    private static CanvasSettings ParseCanvas(
        XElement graph,
        WarningCollector warnings)
    {
        var canvasElement = graph.Element(LegacyNamespace + "canvas");
        if (canvasElement is null)
        {
            warnings.AddOnce(
                "missing-canvas",
                "The legacy canvas was missing; default canvas settings were used.");
            return new CanvasSettings();
        }

        var canvas = new CanvasSettings
        {
            Width = ReadFiniteDouble(canvasElement, "w", 520),
            Height = ReadFiniteDouble(canvasElement, "h", 420),
            BackgroundColor = ReadColor(
                canvasElement,
                "#FFFFFF",
                warnings,
                "canvas background"),
        };

        var whitespace = canvasElement.Element(LegacyNamespace + "whitespace");
        if (whitespace is null)
        {
            canvas.PaddingTop = 2;
            canvas.PaddingRight = 2;
            canvas.PaddingBottom = 2;
            canvas.PaddingLeft = 2;
        }
        else
        {
            canvas.PaddingTop = ReadFiniteDouble(whitespace, "top", 0);
            canvas.PaddingRight = ReadFiniteDouble(whitespace, "right", 0);
            canvas.PaddingBottom = ReadFiniteDouble(whitespace, "bottom", 0);
            canvas.PaddingLeft = ReadFiniteDouble(whitespace, "left", 0);
        }

        return canvas;
    }

    private static AxisSettings ParseAxis(
        XElement graph,
        string dimension,
        Dictionary<string, LegacyLabel> labels,
        HashSet<string> axisLabelIds,
        WarningCollector warnings)
    {
        var matchingAxes = graph
            .Elements(LegacyNamespace + "axis")
            .Where(element => string.Equals(
                (string?)element.Attribute("dimension"),
                dimension,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingAxes.Count == 0)
        {
            warnings.AddOnce(
                $"missing-axis-{dimension}",
                $"The legacy {dimension.ToUpperInvariant()} axis was missing; defaults were used.");
            return new AxisSettings();
        }

        if (matchingAxes.Count > 1)
        {
            warnings.AddOnce(
                $"duplicate-axis-{dimension}",
                $"Multiple legacy {dimension.ToUpperInvariant()} axes were found; only the first was imported.");
        }

        var element = matchingAxes[0];
        var scaleName = ((string?)element.Attribute("scale") ?? "linear").Trim();
        var scale = scaleName.Equals("logarithmic", StringComparison.OrdinalIgnoreCase) ||
                    scaleName.Equals("log", StringComparison.OrdinalIgnoreCase)
            ? AxisScale.Logarithmic
            : AxisScale.Linear;

        if (scale == AxisScale.Linear &&
            !scaleName.Equals("linear", StringComparison.OrdinalIgnoreCase))
        {
            warnings.AddOnce(
                $"axis-scale-{scaleName}",
                $"The legacy axis scale '{scaleName}' is unsupported and was imported as linear.");
        }

        var minimum = ReadFiniteDouble(
            element,
            "min",
            scale == AxisScale.Logarithmic ? 1 : 0);
        var maximum = ReadFiniteDouble(element, "max", 10);
        var reversed = minimum > maximum;
        if (reversed)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        var ticks = element.Element(LegacyNamespace + "ticks");
        double? tickSpacing = null;
        if (ticks is not null)
        {
            var userSpacing = ReadOptionalFiniteDouble(ticks, "user-spacing");
            var ordinarySpacing = ReadOptionalFiniteDouble(ticks, "spacing");
            tickSpacing = userSpacing is > 0 ? userSpacing : ordinarySpacing;
            if (tickSpacing is <= 0)
            {
                tickSpacing = null;
            }

            if (!ReadBoolean(ticks, "visible", defaultValue: true) ||
                string.Equals(
                    (string?)ticks.Attribute("layout"),
                    "hidden",
                    StringComparison.OrdinalIgnoreCase))
            {
                warnings.AddOnce(
                    "tick-visibility",
                    "Legacy tick-mark visibility is not independently representable; tick spacing was preserved.");
            }
        }

        var grid = element.Element(LegacyNamespace + "grid");
        var tickLabels = element.Element(LegacyNamespace + "tick-labels");
        var titleElement = element.Element(LegacyNamespace + "title");
        var title = string.Empty;

        if (titleElement is not null)
        {
            var labelId = ((string?)titleElement.Attribute("label"))?.Trim();
            if (!string.IsNullOrEmpty(labelId))
            {
                axisLabelIds.Add(labelId);
                if (ReadBoolean(titleElement, "visible", defaultValue: true))
                {
                    if (labels.TryGetValue(labelId, out var titleLabel))
                    {
                        title = LimitText(
                            titleLabel.Text,
                            512,
                            warnings,
                            "An axis title");
                    }
                    else
                    {
                        warnings.Add(
                            $"Axis title label '{labelId}' was not found and was omitted.");
                    }
                }
            }
        }

        if (tickLabels is not null)
        {
            var userLabels = tickLabels.Element(LegacyNamespace + "user-labels");
            if (userLabels is not null)
            {
                foreach (var labelReference in userLabels.Elements(
                             LegacyNamespace + "label"))
                {
                    var reference = ((string?)labelReference.Attribute("idref"))?.Trim();
                    if (!string.IsNullOrEmpty(reference))
                    {
                        axisLabelIds.Add(reference);
                    }
                }

                if (userLabels.Elements(LegacyNamespace + "label").Any())
                {
                    warnings.AddOnce(
                        "custom-tick-labels",
                        "Custom legacy tick-label text is not representable and was omitted.");
                }
            }
        }

        var numberFormat = "G4";
        var scientificNotation =
            ((string?)tickLabels?.Attribute("scientific-notation"))?.Trim();
        if (string.Equals(scientificNotation, "on", StringComparison.OrdinalIgnoreCase))
        {
            numberFormat = "0.###E+0";
        }
        else if (string.Equals(
                     scientificNotation,
                     "off",
                     StringComparison.OrdinalIgnoreCase))
        {
            numberFormat = "0.####";
        }

        return new AxisSettings
        {
            Title = title,
            Scale = scale,
            Minimum = minimum,
            Maximum = maximum,
            IsReversed = reversed,
            ShowGridLines = grid is not null &&
                            ReadBoolean(grid, "visible", defaultValue: true),
            ShowAxisLine = ReadBoolean(element, "visible", defaultValue: true),
            ShowTickLabels = tickLabels is not null &&
                             ReadBoolean(tickLabels, "visible", defaultValue: true),
            TickSpacing = tickSpacing,
            DesiredTickCount = CalculateTickCount(
                minimum,
                maximum,
                tickSpacing,
                scale),
            NumberFormat = numberFormat,
            LogarithmBase = 10,
        };
    }

    private static int CalculateTickCount(
        double minimum,
        double maximum,
        double? spacing,
        AxisScale scale)
    {
        if (spacing is not > 0)
        {
            return 6;
        }

        var span = scale == AxisScale.Logarithmic && minimum > 0 && maximum > 0
            ? Math.Log10(maximum / minimum)
            : maximum - minimum;

        if (!double.IsFinite(span) || span <= 0)
        {
            return 6;
        }

        var tickCount = Math.Round(span / spacing.Value) + 1;
        return (int)Math.Clamp(tickCount, 2, 100);
    }

    private static Dictionary<string, LegacyLabel> ParseLabels(
        XElement graph,
        WarningCollector warnings)
    {
        var labels = new Dictionary<string, LegacyLabel>(StringComparer.Ordinal);
        var index = 0;

        foreach (var element in graph.Elements(LegacyNamespace + "label"))
        {
            index++;
            var id = RequireId(element, "label", index);
            var textElement = element.Element(LegacyNamespace + "text");
            var runs = textElement?
                .Descendants(LegacyNamespace + "run")
                .ToList() ?? [];

            if (runs.Count > 1 ||
                runs.Any(run => run.Element(LegacyNamespace + "style") is not null))
            {
                warnings.AddOnce(
                    "rich-text",
                    "Legacy rich text was flattened to plain text.");
            }

            var paragraphs = textElement?
                .Elements(LegacyNamespace + "p")
                .Select(paragraph => string.Concat(
                    paragraph
                        .Descendants(LegacyNamespace + "lit")
                        .Select(literal => literal.Value)))
                .ToList() ?? [];

            var text = paragraphs.Count > 0
                ? string.Join(Environment.NewLine, paragraphs)
                : string.Concat(
                    textElement?
                        .Descendants(LegacyNamespace + "lit")
                        .Select(literal => literal.Value) ?? []);

            var fontSize = ReadFirstFontSize(runs);
            var fontColor = ReadFirstFontColor(runs, warnings);

            labels.Add(
                id,
                new LegacyLabel(
                    id,
                    text,
                    ReadOptionalFiniteDouble(element, "x"),
                    ReadOptionalFiniteDouble(element, "y"),
                    ((string?)element.Attribute("owner"))?.Trim(),
                    ReadBoolean(element, "visible", defaultValue: true),
                    fontSize,
                    fontColor));
        }

        return labels;
    }

    private static List<LegacyVertex> ParseVertices(
        XElement graph,
        Dictionary<string, LegacyLabel> labels,
        WarningCollector warnings)
    {
        var labelsByOwner = labels.Values
            .Where(label => !string.IsNullOrWhiteSpace(label.OwnerId))
            .GroupBy(label => label.OwnerId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.Ordinal);

        var vertices = new List<LegacyVertex>();
        var index = 0;

        foreach (var element in graph.Elements(LegacyNamespace + "vertex"))
        {
            index++;
            var id = RequireId(element, "vertex", index);
            var rawShape = ((string?)element.Attribute("shape") ?? "none").Trim();
            var markerShape = MapMarkerShape(rawShape, warnings);
            var label = default(string);

            if (labelsByOwner.TryGetValue(id, out var ownedLabels))
            {
                label = LimitText(
                    ownedLabels[0].Text,
                    2_048,
                    warnings,
                    $"Point label '{ownedLabels[0].Id}'");

                if (ownedLabels.Count > 1)
                {
                    warnings.Add(
                        $"Vertex '{id}' has multiple labels; only the first was preserved.");
                }
            }

            if (element.Element(LegacyNamespace + "snapped-to") is not null)
            {
                warnings.AddOnce(
                    "snapping",
                    "Legacy snapping relationships were flattened to stored coordinates.");
            }

            vertices.Add(
                new LegacyVertex(
                    id,
                    ReadFiniteDouble(element, "x", 0),
                    ReadFiniteDouble(element, "y", 0),
                    ReadNonNegativeDouble(element, "width", 2),
                    rawShape,
                    markerShape,
                    ReadColor(element, "#000000", warnings, $"vertex '{id}'"),
                    label));
        }

        return vertices;
    }

    private static RecognizedErrorBars RecognizeErrorBars(
        IReadOnlyList<XElement> lines,
        IReadOnlyDictionary<string, LegacyVertex> vertices,
        WarningCollector warnings)
    {
        var recognizedLineIds = new HashSet<string>(StringComparer.Ordinal);
        var endpointVertexIds = new HashSet<string>(StringComparer.Ordinal);
        var errors = new Dictionary<string, ErrorMagnitude>(StringComparer.Ordinal);
        var lineIndex = 0;

        foreach (var line in lines)
        {
            lineIndex++;
            var lineId = RequireId(line, "line", lineIndex);
            if (string.Equals(
                    (string?)line.Attribute("class"),
                    "fit",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    (string?)line.Attribute("method") ?? "curved",
                    "straight",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    (string?)line.Attribute("dash") ?? "solid",
                    "solid",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var references = ReadIdReferences(
                line.Element(LegacyNamespace + "vertices"),
                "ids");
            if (references.Length != 3 ||
                references.Any(reference => !vertices.ContainsKey(reference)))
            {
                continue;
            }

            var candidates = references.Select(reference => vertices[reference]).ToList();
            var tickVertices = candidates
                .Where(vertex => string.Equals(
                    vertex.RawShape,
                    "tickmark",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var middleVertices = candidates
                .Where(vertex => !string.Equals(
                    vertex.RawShape,
                    "tickmark",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (tickVertices.Count != 2 || middleVertices.Count != 1)
            {
                continue;
            }

            var middle = middleVertices[0];
            var firstEnd = tickVertices[0];
            var secondEnd = tickVertices[1];
            ErrorMagnitude? magnitude = null;

            if (NearlyEqual(firstEnd.X, middle.X) &&
                NearlyEqual(secondEnd.X, middle.X))
            {
                var firstDistance = Math.Abs(firstEnd.Y - middle.Y);
                var secondDistance = Math.Abs(secondEnd.Y - middle.Y);
                if (NearlyEqual(firstDistance, secondDistance) &&
                    firstDistance > 0)
                {
                    magnitude = new ErrorMagnitude(
                        XError: null,
                        YError: (firstDistance + secondDistance) / 2);
                }
            }
            else if (NearlyEqual(firstEnd.Y, middle.Y) &&
                     NearlyEqual(secondEnd.Y, middle.Y))
            {
                var firstDistance = Math.Abs(firstEnd.X - middle.X);
                var secondDistance = Math.Abs(secondEnd.X - middle.X);
                if (NearlyEqual(firstDistance, secondDistance) &&
                    firstDistance > 0)
                {
                    magnitude = new ErrorMagnitude(
                        XError: (firstDistance + secondDistance) / 2,
                        YError: null);
                }
            }

            if (magnitude is null)
            {
                warnings.AddOnce(
                    "asymmetric-error-bars",
                    "An asymmetric legacy error bar could not be represented as a symmetric point error and remains an ordinary series.");
                continue;
            }

            recognizedLineIds.Add(lineId);
            endpointVertexIds.Add(firstEnd.Id);
            endpointVertexIds.Add(secondEnd.Id);
            MergeError(errors, middle.Id, magnitude.Value, warnings);
        }

        return new RecognizedErrorBars(
            recognizedLineIds,
            endpointVertexIds,
            errors);
    }

    private static void MergeError(
        IDictionary<string, ErrorMagnitude> errors,
        string vertexId,
        ErrorMagnitude incoming,
        WarningCollector warnings)
    {
        if (!errors.TryGetValue(vertexId, out var existing))
        {
            errors.Add(vertexId, incoming);
            return;
        }

        if (existing.XError is not null && incoming.XError is not null ||
            existing.YError is not null && incoming.YError is not null)
        {
            warnings.Add(
                $"Vertex '{vertexId}' has duplicate error bars; the largest magnitude was preserved.");
        }

        errors[vertexId] = new ErrorMagnitude(
            MaximumNullable(existing.XError, incoming.XError),
            MaximumNullable(existing.YError, incoming.YError));
    }

    private static void ParseLines(
        IReadOnlyList<XElement> lines,
        IReadOnlyDictionary<string, LegacyVertex> vertices,
        Dictionary<string, LegacyLabel> labels,
        RecognizedErrorBars errorBars,
        HashSet<string> consumedVertices,
        HashSet<string> modelIds,
        List<GraphSeries> series,
        WarningCollector warnings)
    {
        var labelsByOwner = GetLabelsByOwner(labels);
        var connectIndex = 0;
        var fitIndex = 0;
        var lineIndex = 0;

        foreach (var line in lines)
        {
            lineIndex++;
            var legacyId = RequireId(line, "line", lineIndex);
            if (errorBars.LineIds.Contains(legacyId))
            {
                continue;
            }

            var lineClass = ((string?)line.Attribute("class") ?? "connect").Trim();
            var isFit = lineClass.Equals("fit", StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<string> references;

            if (isFit)
            {
                fitIndex++;
                var endpoints = new[]
                {
                    ((string?)line.Attribute("v1"))?.Trim(),
                    ((string?)line.Attribute("v2"))?.Trim(),
                };
                references = endpoints
                    .Where(reference => !string.IsNullOrEmpty(reference))
                    .Cast<string>()
                    .ToList();

                if (references.Count != 2)
                {
                    var dataReferences = ReadIdReferences(
                        line.Element(LegacyNamespace + "data"),
                        "ids");
                    references = dataReferences.Length >= 2
                        ? [dataReferences[0], dataReferences[^1]]
                        : dataReferences;
                    warnings.Add(
                        $"Fit line '{legacyId}' did not store both endpoints; its data range was used as a fallback.");
                }
            }
            else
            {
                connectIndex++;
                if (!lineClass.Equals("connect", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(
                        $"Line '{legacyId}' has unsupported class '{lineClass}' and was treated as a connect line.");
                }

                references = ReadIdReferences(
                    line.Element(LegacyNamespace + "vertices"),
                    "ids");
            }

            var points = new List<DataPoint>();
            var referencedVertices = new List<LegacyVertex>();
            foreach (var reference in references)
            {
                if (!vertices.TryGetValue(reference, out var vertex))
                {
                    warnings.Add(
                        $"Line '{legacyId}' references missing vertex '{reference}', which was omitted.");
                    continue;
                }

                consumedVertices.Add(reference);
                referencedVertices.Add(vertex);
                points.Add(ToDataPoint(vertex, errorBars));
            }

            if (points.Count == 0)
            {
                warnings.Add($"Line '{legacyId}' has no importable vertices and was omitted.");
                continue;
            }

            var prototype = referencedVertices[0];
            WarnForMixedVertexStyle(referencedVertices, legacyId, warnings);
            var defaultName = isFit
                ? $"Best fit {fitIndex.ToString(CultureInfo.InvariantCulture)}"
                : $"Series {connectIndex.ToString(CultureInfo.InvariantCulture)}";

            series.Add(
                new GraphSeries
                {
                    Id = MakeModelId(legacyId, "line", lineIndex, modelIds, warnings),
                    Name = ReadOwnedLabel(labelsByOwner, legacyId, defaultName, warnings),
                    FillArea = false,
                    LineStyle = MapLineStyle(
                        (string?)line.Attribute("dash"),
                        warnings),
                    LineMode = isFit
                        ? LineMode.Straight
                        : MapLineMode((string?)line.Attribute("method")),
                    MarkerShape = prototype.MarkerShape,
                    Color = ReadColor(
                        line,
                        "#000000",
                        warnings,
                        $"line '{legacyId}'"),
                    StrokeWidth = ReadNonNegativeDouble(line, "width", 2),
                    MarkerSize = prototype.Width,
                    Points = points,
                });
        }
    }

    private static void ParseFills(
        XElement graph,
        IReadOnlyDictionary<string, LegacyVertex> vertices,
        Dictionary<string, LegacyLabel> labels,
        RecognizedErrorBars errorBars,
        HashSet<string> consumedVertices,
        HashSet<string> modelIds,
        List<GraphSeries> series,
        WarningCollector warnings)
    {
        var labelsByOwner = GetLabelsByOwner(labels);
        var fillIndex = 0;

        foreach (var fill in graph.Elements(LegacyNamespace + "fill"))
        {
            fillIndex++;
            var legacyId = RequireId(fill, "fill", fillIndex);
            var references = ReadIdReferences(
                fill.Element(LegacyNamespace + "vertices"),
                "ids");
            var referencedVertices = new List<LegacyVertex>();

            foreach (var reference in references)
            {
                if (!vertices.TryGetValue(reference, out var vertex))
                {
                    warnings.Add(
                        $"Fill '{legacyId}' references missing vertex '{reference}', which was omitted.");
                    continue;
                }

                consumedVertices.Add(reference);
                referencedVertices.Add(vertex);
            }

            warnings.AddOnce(
                "fills",
                "Legacy fills were simplified to editable area-series boundaries; advanced fill geometry was flattened.");

            if (referencedVertices.Count == 0)
            {
                warnings.Add($"Fill '{legacyId}' has no importable boundary and was omitted.");
                continue;
            }

            var prototype = referencedVertices[0];
            series.Add(
                new GraphSeries
                {
                    Id = MakeModelId(legacyId, "fill", fillIndex, modelIds, warnings),
                    Name = ReadOwnedLabel(
                        labelsByOwner,
                        legacyId,
                        $"Fill {fillIndex.ToString(CultureInfo.InvariantCulture)}",
                        warnings),
                    FillArea = true,
                    LineStyle = LineStyle.None,
                    LineMode = LineMode.Straight,
                    MarkerShape = prototype.MarkerShape,
                    Color = ReadColor(
                        fill,
                        "#00000080",
                        warnings,
                        $"fill '{legacyId}'"),
                    StrokeWidth = 0,
                    MarkerSize = prototype.Width,
                    Points = referencedVertices
                        .Select(vertex => ToDataPoint(vertex, errorBars))
                        .ToList(),
                });
        }
    }

    private static void ParseFreeVertices(
        IReadOnlyList<LegacyVertex> vertices,
        RecognizedErrorBars errorBars,
        HashSet<string> consumedVertices,
        HashSet<string> modelIds,
        List<GraphSeries> series)
    {
        var groups = vertices
            .Where(vertex => !consumedVertices.Contains(vertex.Id))
            .GroupBy(vertex => new VertexStyleKey(
                vertex.Color,
                vertex.MarkerShape,
                vertex.Width))
            .ToList();

        var groupIndex = 0;
        foreach (var group in groups)
        {
            groupIndex++;
            var id = GetUnusedGeneratedId("legacy-points", groupIndex, modelIds);
            series.Add(
                new GraphSeries
                {
                    Id = id,
                    Name = $"Points {groupIndex.ToString(CultureInfo.InvariantCulture)}",
                    FillArea = false,
                    LineStyle = LineStyle.None,
                    LineMode = LineMode.None,
                    MarkerShape = group.Key.MarkerShape,
                    Color = group.Key.Color,
                    StrokeWidth = 0,
                    MarkerSize = group.Key.Width,
                    Points = group
                        .Select(vertex => ToDataPoint(vertex, errorBars))
                        .ToList(),
                });
        }
    }

    private static List<GraphAnnotation> ParseAnnotations(
        Dictionary<string, LegacyLabel> labels,
        HashSet<string> axisLabelIds,
        HashSet<string> modelIds,
        WarningCollector warnings)
    {
        var annotations = new List<GraphAnnotation>();
        var annotationIndex = 0;

        foreach (var label in labels.Values)
        {
            if (!label.Visible ||
                !string.IsNullOrWhiteSpace(label.OwnerId) ||
                axisLabelIds.Contains(label.Id) ||
                string.IsNullOrWhiteSpace(label.Text))
            {
                continue;
            }

            annotationIndex++;
            annotations.Add(
                new GraphAnnotation
                {
                    Id = MakeModelId(
                        label.Id,
                        "annotation",
                        annotationIndex,
                        modelIds,
                        warnings),
                    Kind = AnnotationKind.Text,
                    CoordinateSpace = AnnotationCoordinateSpace.Data,
                    X = label.X ?? 0,
                    Y = label.Y ?? 0,
                    Text = LimitText(
                        label.Text,
                        8_192,
                        warnings,
                        $"Annotation '{label.Id}'"),
                    Color = label.Color,
                    FillColor = "#00000000",
                    FontSize = label.FontSize,
                });
        }

        return annotations;
    }

    private static DataPoint ToDataPoint(
        LegacyVertex vertex,
        RecognizedErrorBars errorBars)
    {
        errorBars.ByVertexId.TryGetValue(vertex.Id, out var error);
        return new DataPoint(vertex.X, vertex.Y, vertex.Label)
        {
            XError = error.XError,
            YError = error.YError,
        };
    }

    private static Dictionary<string, List<LegacyLabel>> GetLabelsByOwner(
        Dictionary<string, LegacyLabel> labels) =>
        labels.Values
            .Where(label => !string.IsNullOrWhiteSpace(label.OwnerId))
            .GroupBy(label => label.OwnerId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.Ordinal);

    private static string ReadOwnedLabel(
        Dictionary<string, List<LegacyLabel>> labelsByOwner,
        string ownerId,
        string defaultValue,
        WarningCollector warnings)
    {
        if (!labelsByOwner.TryGetValue(ownerId, out var labels) ||
            labels.Count == 0 ||
            string.IsNullOrWhiteSpace(labels[0].Text))
        {
            return defaultValue;
        }

        if (labels.Count > 1)
        {
            warnings.Add(
                $"Element '{ownerId}' has multiple labels; only the first was used as its name.");
        }

        return LimitText(labels[0].Text, 512, warnings, $"Element name '{ownerId}'");
    }

    private static void WarnForMixedVertexStyle(
        IReadOnlyList<LegacyVertex> vertices,
        string lineId,
        WarningCollector warnings)
    {
        var prototype = vertices[0];
        if (vertices.Skip(1).Any(vertex =>
                vertex.MarkerShape != prototype.MarkerShape ||
                !string.Equals(
                    vertex.Color,
                    prototype.Color,
                    StringComparison.OrdinalIgnoreCase) ||
                !NearlyEqual(vertex.Width, prototype.Width)))
        {
            warnings.Add(
                $"Line '{lineId}' contains mixed point styles; the first point style was used for the series.");
        }
    }

    private static MarkerShape MapMarkerShape(
        string shape,
        WarningCollector warnings)
    {
        switch (shape.ToLowerInvariant())
        {
            case "none":
                return MarkerShape.None;
            case "circle":
            case "hollow":
                if (shape.Equals("hollow", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.AddOnce(
                        "hollow-marker",
                        "Hollow markers were imported as ordinary circle markers.");
                }

                return MarkerShape.Circle;
            case "square":
                return MarkerShape.Square;
            case "triangle":
            case "arrow":
                if (shape.Equals("arrow", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.AddOnce(
                        "arrow-marker",
                        "Arrow endpoint markers were approximated as triangle markers.");
                }

                return MarkerShape.Triangle;
            case "diamond":
            case "treasure":
                if (shape.Equals("treasure", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.AddOnce(
                        "treasure-marker",
                        "Treasure markers were approximated as diamond markers.");
                }

                return MarkerShape.Diamond;
            case "cross":
                return MarkerShape.Cross;
            case "star":
            case "tickmark":
                if (shape.Equals("star", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.AddOnce(
                        "star-marker",
                        "Star markers were approximated as plus markers.");
                }

                return MarkerShape.Plus;
            case "bar-vertical":
            case "bar-horizontal":
                warnings.AddOnce(
                    "bar-marker",
                    "Legacy bar markers were approximated as square markers.");
                return MarkerShape.Square;
            default:
                warnings.AddOnce(
                    $"marker-{shape}",
                    $"Unsupported legacy marker shape '{shape}' was imported without a marker.");
                return MarkerShape.None;
        }
    }

    private static LineStyle MapLineStyle(
        string? dashName,
        WarningCollector warnings)
    {
        var dash = (dashName ?? "solid").Trim().ToLowerInvariant();
        switch (dash)
        {
            case "solid":
                return LineStyle.Solid;
            case "dots":
                return LineStyle.Dotted;
            case "dashes":
            case "dashes-spaced":
            case "dashes-long":
                return LineStyle.Dashed;
            case "dashes-dots":
                return LineStyle.DashDot;
            case "arrows":
            case "reverse-arrows":
            case "railroad":
                warnings.AddOnce(
                    $"dash-{dash}",
                    $"Legacy line pattern '{dash}' was approximated as a dashed line.");
                return LineStyle.Dashed;
            default:
                warnings.AddOnce(
                    $"dash-{dash}",
                    $"Unsupported legacy line pattern '{dash}' was imported as solid.");
                return LineStyle.Solid;
        }
    }

    private static LineMode MapLineMode(string? methodName) =>
        string.Equals(
            methodName ?? "curved",
            "straight",
            StringComparison.OrdinalIgnoreCase)
            ? LineMode.Straight
            : LineMode.Smooth;

    private static string ReadColor(
        XElement owner,
        string defaultColor,
        WarningCollector warnings,
        string context)
    {
        var color = owner.Element(LegacyNamespace + "color");
        if (color is null)
        {
            return defaultColor;
        }

        double red;
        double green;
        double blue;

        if (color.Attribute("r") is not null &&
            color.Attribute("g") is not null &&
            color.Attribute("b") is not null)
        {
            red = ReadFiniteDouble(color, "r", 0);
            green = ReadFiniteDouble(color, "g", 0);
            blue = ReadFiniteDouble(color, "b", 0);
        }
        else if (color.Attribute("w") is not null)
        {
            red = green = blue = ReadFiniteDouble(color, "w", 0);
        }
        else if (color.Attribute("h") is not null &&
                 color.Attribute("s") is not null &&
                 (color.Attribute("v") is not null ||
                  color.Attribute("b") is not null))
        {
            var hue = ReadFiniteDouble(color, "h", 0);
            var saturation = ReadFiniteDouble(color, "s", 0);
            var value = color.Attribute("v") is not null
                ? ReadFiniteDouble(color, "v", 0)
                : ReadFiniteDouble(color, "b", 0);
            (red, green, blue) = HsvToRgb(hue, saturation, value);
        }
        else if (color.Attribute("c") is not null &&
                 color.Attribute("m") is not null &&
                 color.Attribute("y") is not null &&
                 color.Attribute("k") is not null)
        {
            var cyan = ReadFiniteDouble(color, "c", 0);
            var magenta = ReadFiniteDouble(color, "m", 0);
            var yellow = ReadFiniteDouble(color, "y", 0);
            var black = ReadFiniteDouble(color, "k", 0);
            red = (1 - cyan) * (1 - black);
            green = (1 - magenta) * (1 - black);
            blue = (1 - yellow) * (1 - black);
        }
        else
        {
            warnings.AddOnce(
                "archived-color",
                "A catalog, pattern, or archived legacy color could not be decoded and used a fallback color.");
            return defaultColor;
        }

        var alpha = ReadFiniteDouble(color, "a", 1);
        if (red is < 0 or > 1 ||
            green is < 0 or > 1 ||
            blue is < 0 or > 1 ||
            alpha is < 0 or > 1)
        {
            warnings.AddOnce(
                "clamped-color",
                $"Out-of-range color components were clamped while importing {context}.");
        }

        return ToHexColor(red, green, blue, alpha);
    }

    private static string ReadFirstFontColor(
        IReadOnlyList<XElement> runs,
        WarningCollector warnings)
    {
        foreach (var run in runs)
        {
            var style = run.Element(LegacyNamespace + "style");
            var value = style?
                .Elements(LegacyNamespace + "value")
                .FirstOrDefault(candidate => string.Equals(
                    (string?)candidate.Attribute("key"),
                    "font-fill",
                    StringComparison.OrdinalIgnoreCase));
            if (value is not null)
            {
                return ReadColor(value, "#111827", warnings, "label text");
            }
        }

        return "#111827";
    }

    private static double ReadFirstFontSize(IReadOnlyList<XElement> runs)
    {
        foreach (var run in runs)
        {
            var style = run.Element(LegacyNamespace + "style");
            var value = style?
                .Elements(LegacyNamespace + "value")
                .FirstOrDefault(candidate => string.Equals(
                    (string?)candidate.Attribute("key"),
                    "font-size",
                    StringComparison.OrdinalIgnoreCase));

            if (value is null)
            {
                continue;
            }

            if (double.TryParse(
                    value.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var fontSize) &&
                double.IsFinite(fontSize) &&
                fontSize > 0)
            {
                return fontSize;
            }
        }

        return 14;
    }

    private static (double Red, double Green, double Blue) HsvToRgb(
        double hue,
        double saturation,
        double value)
    {
        hue = ClampUnit(hue);
        saturation = ClampUnit(saturation);
        value = ClampUnit(value);

        var scaledHue = hue * 6;
        var sector = (int)Math.Floor(scaledHue) % 6;
        var fraction = scaledHue - Math.Floor(scaledHue);
        var p = value * (1 - saturation);
        var q = value * (1 - fraction * saturation);
        var t = value * (1 - (1 - fraction) * saturation);

        return sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };
    }

    private static string ToHexColor(
        double red,
        double green,
        double blue,
        double alpha)
    {
        var redByte = ToColorByte(red);
        var greenByte = ToColorByte(green);
        var blueByte = ToColorByte(blue);
        var alphaByte = ToColorByte(alpha);
        var rgb = string.Create(
            CultureInfo.InvariantCulture,
            $"#{redByte:X2}{greenByte:X2}{blueByte:X2}");

        return alphaByte == byte.MaxValue
            ? rgb
            : string.Create(CultureInfo.InvariantCulture, $"{rgb}{alphaByte:X2}");
    }

    private static byte ToColorByte(double value) =>
        (byte)Math.Round(
            ClampUnit(value) * byte.MaxValue,
            MidpointRounding.AwayFromZero);

    private static double ClampUnit(double value) => Math.Clamp(value, 0, 1);

    private static double ReadNonNegativeDouble(
        XElement element,
        string attributeName,
        double defaultValue)
    {
        var value = ReadFiniteDouble(element, attributeName, defaultValue);
        if (value < 0)
        {
            throw InvalidAttribute(
                element,
                attributeName,
                "must be non-negative");
        }

        return value;
    }

    private static double ReadFiniteDouble(
        XElement element,
        string attributeName,
        double defaultValue)
    {
        var attribute = element.Attribute(attributeName);
        return attribute is null
            ? defaultValue
            : ParseFiniteDouble(element, attribute);
    }

    private static double? ReadOptionalFiniteDouble(
        XElement element,
        string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        return attribute is null
            ? null
            : ParseFiniteDouble(element, attribute);
    }

    private static double ParseFiniteDouble(
        XElement element,
        XAttribute attribute)
    {
        if (!double.TryParse(
                attribute.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            !double.IsFinite(value))
        {
            throw InvalidAttribute(
                element,
                attribute.Name.LocalName,
                "must be a finite number");
        }

        return value;
    }

    private static bool ReadBoolean(
        XElement element,
        string attributeName,
        bool defaultValue)
    {
        var value = ((string?)element.Attribute(attributeName))?.Trim();
        if (value is null)
        {
            return defaultValue;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value == "1")
        {
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value == "0")
        {
            return false;
        }

        throw InvalidAttribute(
            element,
            attributeName,
            "must be a Boolean value");
    }

    private static InvalidDataException InvalidAttribute(
        XElement element,
        string attributeName,
        string requirement)
    {
        var lineInformation = (IXmlLineInfo)element;
        var location = lineInformation.HasLineInfo()
            ? $" at line {lineInformation.LineNumber.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        return new InvalidDataException(
            $"Attribute '{attributeName}' on legacy element '{element.Name.LocalName}'" +
            $"{location} {requirement}.");
    }

    private static string RequireId(
        XElement element,
        string elementKind,
        int index)
    {
        var id = ((string?)element.Attribute("id"))?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidDataException(
                $"Legacy {elementKind} #{index.ToString(CultureInfo.InvariantCulture)} is missing its id.");
        }

        return id;
    }

    private static string[] ReadIdReferences(
        XElement? element,
        string attributeName)
    {
        var value = (string?)element?.Attribute(attributeName);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
    }

    private static void EnsureUniqueLegacyIds(XElement graph)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in graph.Elements())
        {
            var id = ((string?)element.Attribute("id"))?.Trim();
            if (!string.IsNullOrEmpty(id) && !ids.Add(id))
            {
                throw new InvalidDataException(
                    $"The legacy identifier '{id}' is duplicated.");
            }
        }
    }

    private static string MakeModelId(
        string legacyId,
        string kind,
        int index,
        HashSet<string> modelIds,
        WarningCollector warnings)
    {
        if (legacyId.Length <= 128 && modelIds.Add(legacyId))
        {
            return legacyId;
        }

        warnings.Add(
            $"Legacy identifier '{legacyId}' could not be used directly and was replaced.");
        return GetUnusedGeneratedId($"legacy-{kind}", index, modelIds);
    }

    private static string GetUnusedGeneratedId(
        string prefix,
        int initialIndex,
        HashSet<string> modelIds)
    {
        var index = initialIndex;
        while (true)
        {
            var candidate =
                $"{prefix}-{index.ToString(CultureInfo.InvariantCulture)}";
            if (modelIds.Add(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static string GetDocumentTitle(string? sourceName)
    {
        var title = string.IsNullOrWhiteSpace(sourceName)
            ? "Imported Graph"
            : Path.GetFileNameWithoutExtension(sourceName.Trim());
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Imported Graph";
        }

        return title.Length <= 512 ? title : title[..512];
    }

    private static string LimitText(
        string value,
        int maximumLength,
        WarningCollector warnings,
        string context)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        warnings.Add(
            $"{context} exceeded {maximumLength.ToString(CultureInfo.InvariantCulture)} characters and was truncated.");
        return value[..maximumLength];
    }

    private static byte[] ReadWithLimit(
        Stream stream,
        long maximumBytes,
        string errorMessage)
    {
        using var result = new MemoryStream();
        var buffer = new byte[81_920];
        long total = 0;

        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return result.ToArray();
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException(errorMessage);
            }

            result.Write(buffer, 0, read);
        }
    }

    private static bool HasZipSignature(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
        {
            return false;
        }

        return bytes[2] switch
        {
            0x03 when bytes[3] == 0x04 => true,
            0x05 when bytes[3] == 0x06 => true,
            0x07 when bytes[3] == 0x08 => true,
            _ => false,
        };
    }

    private static bool NearlyEqual(double first, double second)
    {
        var scale = Math.Max(1, Math.Max(Math.Abs(first), Math.Abs(second)));
        return Math.Abs(first - second) <= 1e-5 * scale;
    }

    private static double? MaximumNullable(double? first, double? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return Math.Max(first.Value, second.Value);
    }

    private sealed record LegacyLabel(
        string Id,
        string Text,
        double? X,
        double? Y,
        string? OwnerId,
        bool Visible,
        double FontSize,
        string Color);

    private sealed record LegacyVertex(
        string Id,
        double X,
        double Y,
        double Width,
        string RawShape,
        MarkerShape MarkerShape,
        string Color,
        string? Label);

    private readonly record struct VertexStyleKey(
        string Color,
        MarkerShape MarkerShape,
        double Width);

    private readonly record struct ErrorMagnitude(
        double? XError,
        double? YError);

    private sealed record RecognizedErrorBars(
        HashSet<string> LineIds,
        HashSet<string> EndpointVertexIds,
        Dictionary<string, ErrorMagnitude> ByVertexId);

    private sealed class WarningCollector
    {
        private readonly List<string> _warnings = [];
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private bool _limitWarningAdded;

        public void Add(string warning)
        {
            if (_warnings.Count < MaximumWarningCount)
            {
                _warnings.Add(warning);
            }
            else if (!_limitWarningAdded)
            {
                _warnings[^1] =
                    "Additional compatibility warnings were omitted.";
                _limitWarningAdded = true;
            }
        }

        public void AddOnce(string key, string warning)
        {
            if (_keys.Add(key))
            {
                Add(warning);
            }
        }

        public string[] ToArray() => [.. _warnings];
    }
}
