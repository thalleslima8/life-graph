using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>The limits of the Account export (DA-248, DA-256), from the <c>AccountExport</c> section.</summary>
public sealed class AccountExportOptions : IValidatableObject
{
    public const string SectionName = "AccountExport";

    /// <summary>The default cap on the archive size: 50 MiB.</summary>
    public const long DefaultMaxArchiveBytes = 50L * BytesPerMebibyte;

    /// <summary>The default number of rows read per query.</summary>
    public const int DefaultReadBatchSize = 500;

    /// <summary>The largest batch configuration may ask for, so one query stays bounded (DB-023).</summary>
    public const int MaxReadBatchSize = 10_000;

    /// <summary>The longest a ready archive can be downloaded (DA-248).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    private const long BytesPerMebibyte = 1024 * 1024;

    /// <summary>The largest archive built; a larger one fails with <c>size_cap</c>, never truncated.</summary>
    [Range(1, long.MaxValue)]
    public long MaxArchiveBytes { get; set; } = DefaultMaxArchiveBytes;

    /// <summary>
    /// How many rows the export reads per query (DB-023, DA-256): every table is read in keyset
    /// batches of this size, so no query is unbounded and no table has a row limit.
    /// </summary>
    [Range(1, MaxReadBatchSize)]
    public int ReadBatchSize { get; set; } = DefaultReadBatchSize;

    /// <summary>How long a ready archive can be downloaded: at most <see cref="MaxLifetime"/>.</summary>
    public TimeSpan Lifetime { get; set; } = MaxLifetime;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lifetime <= TimeSpan.Zero || Lifetime > MaxLifetime)
        {
            yield return new ValidationResult($"The export lifetime must be more than zero and at most {MaxLifetime.TotalHours} hours.", [nameof(Lifetime)]);
        }
    }
}

/// <summary>
/// The one byte budget of an export (DA-256): every batch written to the archive is charged
/// here, by the reader as it goes and by the archive when it closes, so the export fails the
/// same way wherever the cap is passed, naming the data that passed it.
/// </summary>
internal sealed class AccountExportSizeBudget(long maxBytes)
{
    public long MaxBytes => maxBytes;

    public long UsedBytes { get; private set; }

    /// <summary>The data whose batch took the archive past the cap; <c>null</c> while within it.</summary>
    public AccountExportData? OversizedData { get; private set; }

    public bool IsExceeded => OversizedData is not null;

    /// <summary>Charges <paramref name="bytes"/> written for <paramref name="data"/>; <c>false</c> once past the cap.</summary>
    public bool Charge(AccountExportData data, long bytes)
    {
        if (IsExceeded)
        {
            return false;
        }

        UsedBytes += bytes;
        if (UsedBytes > maxBytes)
        {
            OversizedData = data;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Writes the archive as the reader streams batches into it (DA-248, DA-256):
/// <c>export.json</c>, then <c>nodes/{id}.md</c> per Node, in a zip. Each batch is flushed to
/// the zip and the growth charged to the <see cref="AccountExportSizeBudget"/>, so the running
/// size only ever falls short of the final one: it fails the export early, never wrongly. The
/// size after the zip closes is the one that decides.
/// </summary>
internal sealed class AccountExportArchiveWriter : IDisposable
{
    public const string DocumentEntry = "export.json";

    public const string NodesFolder = "nodes/";

    private static readonly JsonSerializerOptions DocumentOptions = new(GraphWireJson.Options) { WriteIndented = true };

    private readonly AccountExportSizeBudget budget;
    private readonly MemoryStream buffer = new();
    private readonly ZipArchive zip;
    private readonly Stream documentStream;
    private readonly Utf8JsonWriter json;
    private long chargedBytes;
    private AccountExportData lastData = AccountExportData.PropertyDefinitions;

    public AccountExportArchiveWriter(AccountExportSizeBudget budget)
    {
        this.budget = budget;
        zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true);
        documentStream = zip.CreateEntry(DocumentEntry, CompressionLevel.Optimal).Open();
        json = new Utf8JsonWriter(documentStream, new JsonWriterOptions { Indented = true });
    }

    public void WriteHeader(DateTimeOffset exportedAt, ExportedAccount account)
    {
        json.WriteStartObject();
        WriteProperty("FormatVersion", AccountExportDocument.CurrentFormatVersion);
        WriteProperty("ExportedAt", exportedAt);
        WriteProperty("Account", account);
        WriteProperty("Omitted", AccountExportDocument.OmittedData);
    }

    public void BeginSection(AccountExportData data)
    {
        lastData = data;
        json.WritePropertyName(NameOf(data.ToString()));
        json.WriteStartArray();
    }

    /// <summary>Writes one batch of the open section; <c>false</c> once the archive is past the cap.</summary>
    public bool WriteBatch<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            JsonSerializer.Serialize(json, item, DocumentOptions);
        }

        json.Flush();
        documentStream.Flush();
        return ChargeGrowth(lastData);
    }

    public void EndSection() => json.WriteEndArray();

    /// <summary>
    /// Closes <c>export.json</c>, writes the Markdown of every Node and closes the zip.
    /// <c>null</c> when the archive passes the cap: the export fails whole.
    /// </summary>
    public byte[]? Finish(AccountExportGraph graph)
    {
        json.WriteEndObject();
        json.Dispose();
        documentStream.Dispose();
        if (!ChargeGrowth(lastData))
        {
            return null;
        }

        var markdown = new AccountExportMarkdown(graph);
        foreach (var node in graph.Nodes)
        {
            using (var entry = zip.CreateEntry(NodesFolder + AccountExportMarkdown.FileNameOf(node.Id), CompressionLevel.Optimal).Open())
            {
                entry.Write(Encoding.UTF8.GetBytes(markdown.Write(node)));
            }

            if (!ChargeGrowth(AccountExportData.NodeMarkdown))
            {
                return null;
            }
        }

        zip.Dispose();
        return ChargeGrowth(graph.Nodes.Count > 0 ? AccountExportData.NodeMarkdown : lastData) ? buffer.ToArray() : null;
    }

    public void Dispose()
    {
        json.Dispose();
        zip.Dispose();
        buffer.Dispose();
    }

    private bool ChargeGrowth(AccountExportData data)
    {
        var growth = buffer.Length - chargedBytes;
        chargedBytes = buffer.Length;
        return budget.Charge(data, growth);
    }

    private void WriteProperty<T>(string clrName, T value)
    {
        json.WritePropertyName(NameOf(clrName));
        JsonSerializer.Serialize(json, value, DocumentOptions);
    }

    private static string NameOf(string clrName) => DocumentOptions.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;
}
