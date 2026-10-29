using System.Text.RegularExpressions;

namespace Infrastructure.Persistence.Seed.MaturityBaseline;

public sealed record LocalAuthorityCandidate(Guid OrganisationId, string Name, string Region);

public sealed record MatchedBaselineRow(MaturityBaselineRow Row, Guid OrganisationId, string OrganisationName, bool RegionMismatch);

public sealed record UnmatchedBaselineRow(MaturityBaselineRow Row, string Reason);

public sealed record SkippedBaselineRow(MaturityBaselineRow Row, string Reason);

public sealed record LocalAuthorityMatchResult(
    IReadOnlyList<MatchedBaselineRow> Matched,
    IReadOnlyList<UnmatchedBaselineRow> Unmatched,
    IReadOnlyList<SkippedBaselineRow> Skipped);

// Pairs a survey row with a local-authority organisation by NAME (BLUM-7857 AC2). The survey typed names freely
// ("Barking and Dagenham London Borough", "Bradford Metropolitan District Council (City of)", "Cornwall and Isles of
// Scilly;") while the portal's organisations follow the ONS list ("Bristol, City of"), so both sides are reduced to
// the same key: lower-cased, punctuation and bracketed asides dropped, the council-type words stripped, and a few
// known aliases folded. A key that two organisations share is ambiguous and is NOT guessed — the audit names it.
public static partial class LocalAuthorityNameMatcher
{
    public const string NoOrganisation = "no_organisation";

    public const string Ambiguous = "ambiguous";

    public const string BlankName = "blank_name";

    public const string DuplicateRow = "duplicate_row";

    // Longest first, so "metropolitan borough council" goes before "borough council" goes before "council".
    private static readonly string[] SuffixTokens =
    [
        "metropolitan borough council",
        "metropolitan district council",
        "co operative council",
        "london borough council",
        "royal borough council",
        "borough council",
        "county council",
        "district council",
        "city council",
        "unitary authority",
        "london borough",
        "royal borough",
        "corporation",
        "council",
        "city of",
        "county of",
    ];

    private static readonly string[] PrefixTokens =
    [
        "london borough of",
        "royal borough of",
        "borough of",
        "city of",
        "the",
    ];

    // Survey spellings that no amount of suffix stripping reconciles with the ONS name.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["kingston upon hull"] = "hull",
        ["county durham"] = "durham",
        ["saint helens"] = "st helens",
        ["cornwall and isles of scilly"] = "cornwall",
        ["bournemouth christchurch and poole"] = "bournemouth christchurch and poole",
    };

    public static string Normalise(string name)
    {
        var key = name.Trim().ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        key = Apostrophes().Replace(key, string.Empty);
        key = Bracketed().Replace(key, " ");
        key = OnsSuffix().Replace(key, " ");
        key = NonAlphanumeric().Replace(key, " ");
        key = Whitespace().Replace(key, " ").Trim();

        var changed = true;
        while (changed && key.Length > 0)
        {
            changed = false;
            foreach (var token in SuffixTokens)
            {
                if (key.EndsWith(" " + token, StringComparison.Ordinal))
                {
                    key = key[..^(token.Length + 1)].TrimEnd();
                    changed = true;
                }
            }

            foreach (var token in PrefixTokens)
            {
                if (key.StartsWith(token + " ", StringComparison.Ordinal))
                {
                    key = key[(token.Length + 1)..].TrimStart();
                    changed = true;
                }
            }
        }

        return Aliases.TryGetValue(key, out var alias) ? alias : key;
    }

    public static LocalAuthorityMatchResult Match(
        IReadOnlyList<MaturityBaselineRow> rows,
        IReadOnlyList<LocalAuthorityCandidate> organisations)
    {
        var byKey = organisations
            .GroupBy(o => Normalise(o.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var matched = new List<MatchedBaselineRow>();
        var unmatched = new List<UnmatchedBaselineRow>();
        var skipped = new List<SkippedBaselineRow>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var key = Normalise(row.LocalAuthority);
            if (key.Length == 0)
            {
                skipped.Add(new SkippedBaselineRow(row, BlankName));
                continue;
            }

            if (!seenKeys.Add(key))
            {
                skipped.Add(new SkippedBaselineRow(row, DuplicateRow));
                continue;
            }

            if (!byKey.TryGetValue(key, out var candidates))
            {
                unmatched.Add(new UnmatchedBaselineRow(row, NoOrganisation));
                continue;
            }

            if (candidates.Count > 1)
            {
                unmatched.Add(new UnmatchedBaselineRow(row, Ambiguous));
                continue;
            }

            var organisation = candidates[0];
            var regionMismatch = !string.Equals(organisation.Region.Trim(), row.Region.Trim(), StringComparison.OrdinalIgnoreCase);
            matched.Add(new MatchedBaselineRow(row, organisation.OrganisationId, organisation.Name, regionMismatch));
        }

        return new LocalAuthorityMatchResult(matched, unmatched, skipped);
    }

    [GeneratedRegex("['’]")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Bracketed();

    // The ONS list's ", City of" / ", County of" tail ("Bristol, City of", "Herefordshire, County of").
    [GeneratedRegex(@",\s*(city|county) of\s*$")]
    private static partial Regex OnsSuffix();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}