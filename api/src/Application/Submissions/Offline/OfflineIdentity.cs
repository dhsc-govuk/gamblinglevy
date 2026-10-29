using System.Security.Cryptography;
using System.Text;

namespace Application.Submissions.Offline;

// The identity cell in row 3 of every offline file:
//   offline-template|v1|{moduleCode}|{collectionId:N}|{organisationId:N}|{fingerprint}
// It is how an upload knows which section a file is for (one upload control takes all three), refuses a file from
// another report or organisation, and notices a file whose layout no longer matches the report — an activity added,
// removed or moved to another status since the download. v1 is the layout version: a file from a different
// version is out of date rather than misread.
public sealed record OfflineIdentity(string Version, string ModuleCode, Guid CollectionId, Guid OrganisationId, string Fingerprint)
{
    public const string Prefix = "offline-template";
    public const string CurrentVersion = "v1";

    public static string Format(string moduleCode, Guid collectionId, Guid organisationId, string fingerprint) =>
        string.Join('|', Prefix, CurrentVersion, moduleCode, collectionId.ToString("N"), organisationId.ToString("N"), fingerprint);

    public static bool LooksLikeIdentity(string cell) =>
        cell.Trim().StartsWith(Prefix + "|", StringComparison.OrdinalIgnoreCase);

    public static OfflineIdentity? TryParse(string cell)
    {
        var parts = cell.Trim().Split('|');
        if (parts.Length != 6
            || !parts[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[1])
            || string.IsNullOrWhiteSpace(parts[2])
            || !Guid.TryParseExact(parts[3], "N", out var collectionId)
            || !Guid.TryParseExact(parts[4], "N", out var organisationId)
            || !IsFingerprint(parts[5].Trim()))
        {
            return null;
        }

        return new OfflineIdentity(parts[1].Trim(), parts[2].Trim(), collectionId, organisationId, parts[5].Trim());
    }

    private static bool IsFingerprint(string value) =>
        value.Length == 12 && value.All(char.IsAsciiHexDigit);

    // The first 12 hex characters of a SHA-256 over the layout's canonical lines. Callers pass lines that do not
    // depend on order, wording or saved answers, so only a change to which rows and columns exist changes it.
    public static string ComputeFingerprint(IEnumerable<string> lines)
    {
        var canonical = string.Join('\n', lines.OrderBy(line => line, StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}