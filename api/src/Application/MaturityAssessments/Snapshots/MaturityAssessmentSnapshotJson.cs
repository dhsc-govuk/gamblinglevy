using System.Text.Json;

namespace Application.MaturityAssessments.Snapshots;

// Shared JSON (de)serialisation for the frozen assessment snapshot — one set of options for every writer and
// reader, the ReportSnapshotJson precedent.
public static class MaturityAssessmentSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(MaturityAssessmentSnapshotDocument snapshot) => JsonSerializer.Serialize(snapshot, Options);

    // Throws on malformed JSON: a single organisation's own read treats an undecodable snapshot as a fault.
    public static MaturityAssessmentSnapshotDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize<MaturityAssessmentSnapshotDocument>(json, Options);

    // Null when the JSON is null, empty or undeserialisable — for the consolidated export, where one corrupt row
    // must not fail the whole window ZIP.
    public static MaturityAssessmentSnapshotDocument? TryRead(string? json)
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