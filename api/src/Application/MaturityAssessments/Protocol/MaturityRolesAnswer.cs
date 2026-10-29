using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Submissions.Protocol;

namespace Application.MaturityAssessments.Protocol;

// The "Funded roles" repeater's answer: one JSON array holding every levy-funded role and the share of its time the
// funding pays for. One stored value rather than N question codes because the row count is the LA's to choose
// (Role 1 / Role 2 / Add another role) and the quarterly pre-fill produces one entry per People-and-roles activity.
public static class MaturityRolesAnswer
{
    public const int MaxRoles = 20;

    public const int MaxOtherRoleLength = 255;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public sealed record Entry(string Role, string Time, string? OtherRole);

    // Tolerant of the client's raw shape (missing/extra keys, nulls); false only when the value is not a JSON
    // array of objects at all.
    public static bool TryParse(string? json, out IReadOnlyList<Entry> entries)
    {
        entries = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<RawEntry>>(json, Options);
            if (parsed is null)
            {
                return false;
            }

            entries = parsed
                .Select(e => new Entry((e.Role ?? string.Empty).Trim(), (e.Time ?? string.Empty).Trim(), Normalise(e.OtherRole)))
                .ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Canonical JSON so two saves of the same roles compare equal and the snapshot/CSV parse one shape.
    public static string Serialize(IReadOnlyList<Entry> entries) =>
        JsonSerializer.Serialize(entries.Select(e => new RawEntry { Role = e.Role, Time = e.Time, OtherRole = e.OtherRole }).ToList(), Options);

    // A row whose role is the question's ExclusiveOptionValue — the funded roles' "None" (BLUM-9112). It answers the
    // question on its own: it must be the only row, asks no time and is stored with neither time nor other-role name.
    // A question that declares no exclusive value has no such row, so its "none" is just a value to check.
    public static bool IsExclusive(Entry entry, string? exclusiveValue) =>
        exclusiveValue is not null && entry.Role == exclusiveValue;

    public static bool IsExclusiveAnswer(IReadOnlyList<Entry> entries, string? exclusiveValue) =>
        entries.Count == 1 && IsExclusive(entries[0], exclusiveValue);

    // The stored shape: an exclusive row keeps no time or other-role name, whatever the request carried.
    public static IReadOnlyList<Entry> Canonical(IReadOnlyList<Entry> entries, string? exclusiveValue) =>
        entries.Select(e => IsExclusive(e, exclusiveValue) ? e with { Time = string.Empty, OtherRole = null } : e).ToList();

    // "Role 1: Public Health Officer (81% to 100%); Role 2: Other role – Data analyst (41% to 60%)" — the
    // check-your-answers / snapshot rendering when the roles are shown as one cell.
    public static string Render(
        IReadOnlyList<Entry> entries,
        IReadOnlyList<ResolvedQuestionOption> roleOptions,
        IReadOnlyList<ResolvedQuestionOption> timeOptions,
        string? exclusiveValue)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        if (IsExclusiveAnswer(entries, exclusiveValue))
        {
            return LabelFor(roleOptions, entries[0].Role);
        }

        return string.Join("; ", entries.Select((entry, index) =>
            $"Role {index + 1}: {RoleLabel(entry, roleOptions)} ({LabelFor(timeOptions, entry.Time)})"));
    }

    public static string RoleLabel(Entry entry, IReadOnlyList<ResolvedQuestionOption> roleOptions)
    {
        var label = LabelFor(roleOptions, entry.Role);
        return entry.Role == MaturityOptionValues.Other && !string.IsNullOrWhiteSpace(entry.OtherRole)
            ? $"{label} – {entry.OtherRole}"
            : label;
    }

    public static string LabelFor(IReadOnlyList<ResolvedQuestionOption> options, string value) =>
        options.FirstOrDefault(o => o.Value == value)?.Label ?? value;

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class RawEntry
    {
        public string? Role { get; set; }

        public string? Time { get; set; }

        public string? OtherRole { get; set; }
    }
}