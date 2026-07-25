using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

/// <summary>
/// Reads and writes the portable JSON graph document format.
/// </summary>
public static class DocumentSerializer
{
    public const int MaximumDocumentBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(indented: true);

    public const string DefaultFileExtension = ".graphsketch";

    private const int MaximumJsonDepth = 64;
    private const int ReadBufferSize = 81_920;

    public static string Serialize(GraphDocument document, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.EnsureValid();
        return JsonSerializer.Serialize(document, indented ? IndentedOptions : CompactOptions);
    }

    public static GraphDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (json.Length > MaximumDocumentBytes)
        {
            throw DocumentTooLarge();
        }

        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaximumDocumentBytes)
        {
            throw DocumentTooLarge();
        }

        return DeserializeUtf8(Encoding.UTF8.GetBytes(json));
    }

    public static GraphDocument Clone(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Deserialize(Serialize(document, indented: false));
    }

    public static async Task SaveAsync(
        GraphDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        document.EnsureValid();

        await JsonSerializer.SerializeAsync(
                destination,
                document,
                IndentedOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task SaveAsync(
        GraphDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        document.EnsureValid();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16_384,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await SaveAsync(document, stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<GraphDocument> LoadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        }

        var bytes = await ReadDocumentBytesAsync(source, cancellationToken).ConfigureAwait(false);
        return DeserializeUtf8(bytes);
    }

    public static async Task<GraphDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static byte[] SerializeToUtf8(GraphDocument document, bool indented = false)
    {
        var json = Serialize(document, indented);
        return Encoding.UTF8.GetBytes(json);
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            AllowTrailingCommas = true,
            MaxDepth = MaximumJsonDepth,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = indented,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static GraphDocument DeserializeUtf8(ReadOnlySpan<byte> json)
    {
        try
        {
            ValidateJsonResourceLimits(json);
            var document = JsonSerializer.Deserialize<GraphDocument>(json, CompactOptions)
                ?? throw new InvalidDataException("The JSON document did not contain a graph.");
            document.EnsureValid();
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The graph document contains malformed JSON or an unsupported value.",
                exception);
        }
    }

    private static async Task<byte[]> ReadDocumentBytesAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var remainingLength = TryGetRemainingLength(source);
        if (remainingLength > MaximumDocumentBytes)
        {
            throw DocumentTooLarge();
        }

        var initialCapacity = remainingLength is >= 0 and <= MaximumDocumentBytes
            ? (int)remainingLength.Value
            : 0;
        using var buffered = new MemoryStream(initialCapacity);
        var buffer = new byte[ReadBufferSize];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return buffered.ToArray();
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumDocumentBytes)
            {
                throw DocumentTooLarge();
            }

            await buffered
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static long? TryGetRemainingLength(Stream source)
    {
        if (!source.CanSeek)
        {
            return null;
        }

        try
        {
            var remainingLength = source.Length - source.Position;
            return remainingLength >= 0 ? remainingLength : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void ValidateJsonResourceLimits(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = MaximumJsonDepth,
            });

        var pendingCollection = JsonCollection.None;
        var seriesArrayDepth = -1;
        var pointsArrayDepth = -1;
        var annotationsArrayDepth = -1;
        var seriesCount = 0;
        var pointCount = 0;
        var annotationCount = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                pendingCollection = JsonCollection.None;
                if (reader.CurrentDepth == 1)
                {
                    if (PropertyNameEquals(ref reader, "series"))
                    {
                        pendingCollection = JsonCollection.Series;
                    }
                    else if (PropertyNameEquals(ref reader, "annotations"))
                    {
                        pendingCollection = JsonCollection.Annotations;
                    }
                }
                else if (
                    seriesArrayDepth >= 0 &&
                    reader.CurrentDepth == seriesArrayDepth + 2 &&
                    PropertyNameEquals(ref reader, "points"))
                {
                    pendingCollection = JsonCollection.Points;
                }

                continue;
            }

            if (IsJsonValueStart(reader.TokenType))
            {
                if (
                    seriesArrayDepth >= 0 &&
                    reader.CurrentDepth == seriesArrayDepth + 1)
                {
                    AddCollectionItem(
                        ref seriesCount,
                        GraphDocument.MaximumSeriesCount,
                        "series");
                }

                if (
                    pointsArrayDepth >= 0 &&
                    reader.CurrentDepth == pointsArrayDepth + 1)
                {
                    AddCollectionItem(
                        ref pointCount,
                        GraphDocument.MaximumTotalPointCount,
                        "total points");
                }

                if (
                    annotationsArrayDepth >= 0 &&
                    reader.CurrentDepth == annotationsArrayDepth + 1)
                {
                    AddCollectionItem(
                        ref annotationCount,
                        GraphDocument.MaximumAnnotationCount,
                        "annotations");
                }
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                switch (pendingCollection)
                {
                    case JsonCollection.Series:
                        seriesArrayDepth = reader.CurrentDepth;
                        break;
                    case JsonCollection.Points:
                        pointsArrayDepth = reader.CurrentDepth;
                        break;
                    case JsonCollection.Annotations:
                        annotationsArrayDepth = reader.CurrentDepth;
                        break;
                }
            }
            else if (reader.TokenType == JsonTokenType.EndArray)
            {
                if (reader.CurrentDepth == pointsArrayDepth)
                {
                    pointsArrayDepth = -1;
                }

                if (reader.CurrentDepth == seriesArrayDepth)
                {
                    seriesArrayDepth = -1;
                }

                if (reader.CurrentDepth == annotationsArrayDepth)
                {
                    annotationsArrayDepth = -1;
                }
            }

            pendingCollection = JsonCollection.None;
        }
    }

    private static bool PropertyNameEquals(
        ref Utf8JsonReader reader,
        string expectedName) =>
        reader.ValueTextEquals(expectedName) ||
        string.Equals(
            reader.GetString(),
            expectedName,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonValueStart(JsonTokenType tokenType) =>
        tokenType is
            JsonTokenType.StartObject or
            JsonTokenType.StartArray or
            JsonTokenType.String or
            JsonTokenType.Number or
            JsonTokenType.True or
            JsonTokenType.False or
            JsonTokenType.Null;

    private static void AddCollectionItem(
        ref int count,
        int maximumCount,
        string collectionName)
    {
        count++;
        if (count > maximumCount)
        {
            throw new InvalidDataException(
                "The graph document exceeds the limit of " +
                $"{maximumCount.ToString("N0", CultureInfo.InvariantCulture)} " +
                $"{collectionName}.");
        }
    }

    private static InvalidDataException DocumentTooLarge() =>
        new(
            $"The graph document exceeds the {MaximumDocumentBytes / (1024 * 1024)} MiB input limit.");

    private enum JsonCollection
    {
        None,
        Series,
        Points,
        Annotations,
    }
}
