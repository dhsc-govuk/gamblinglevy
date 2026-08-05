using System.Text.Json;

namespace Application.Common.ReportSnapshots;

// Shared JSON (de)serialisation for the frozen report snapshot, so the value persisted at submit time round-trips
// exactly at export time. One set of options for every writer and reader.
public static class ReportSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ReportSnapshotDocument snapshot) => JsonSerializer.Serialize(snapshot, Options);

    // Throws on malformed JSON: the per-organisation export paths treat an undecodable snapshot as a fault, not as
    // "no content", and swallowing it there would hand the LA a silently empty copy of its own report.
    public static ReportSnapshotDocument? Deserialize(string json) => JsonSerializer.Deserialize<ReportSnapshotDocument>(json, Options);

    // Null when the JSON is null, empty or undeserialisable. Used by the cross-organisation export, where one corrupt
    // row must not fail the whole window ZIP — that organisation is omitted from the section files and stays in the
    // summary. Deliberately the narrower contract; everyone else uses Deserialize and lets the exception surface.
    public static ReportSnapshotDocument? TryRead(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return Deserialize(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}