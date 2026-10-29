using System.Globalization;

namespace Application.Common.Funding;

/// <summary>
/// The UK financial year as this codebase spells it: a "YYYY/YY" string ("2026/27"), never a value object.
/// funding_windows.financial_year and collections.financial_year are both varchar(16) and every lookup that
/// reaches them (IFundingWindowRepository.FindByFinancialYearAsync, ISubmissionQueries.GetForOrgAndFinancialYearAsync)
/// matches by exact string equality — so the FORMAT is the contract, and one place has to own it.
///
/// It used to live as a private copy inside Infrastructure.Funding.FundingWindowResolver, which made it both
/// untestable from the unit project and invisible to the Application-layer validators that now have to reject a
/// malformed year before it reaches a window.
/// </summary>
public static class FinancialYearRange
{
    /// <summary>
    /// The financial year containing <paramref name="nowUtc"/>. The UK government financial year runs
    /// 1 April - 31 March, so a date before 1 April belongs to the previous calendar year's FY.
    ///
    /// This moved from 6 April when it was lifted out of FundingWindowResolver (BLUM-7608). 6 April is the
    /// PERSONAL TAX year, not the financial year a levy allocation is made for, and the rest of the codebase
    /// already used 1 April: Application.Collections.ReportingPeriod.Derive bounds a financial year at
    /// 1 April - 31 March and maps Q1 to April-June, and the client's quarterOptions says the same. The two
    /// disagreed for five days a year, during which an organisation onboarded on, say, 3 April was allocated
    /// against the year that had just ended while its own quarterly reports were already being authored
    /// against the new one.
    /// </summary>
    public static string Current(DateTime nowUtc)
    {
        var fyStartYear = nowUtc.Month >= 4 ? nowUtc.Year : nowUtc.Year - 1;

        return Format(fyStartYear);
    }

    /// <summary>
    /// The financial year after <paramref name="financialYear"/>. Throws on a value IsValid would reject, because
    /// every caller has already validated (the command validators) or is deriving from Current above.
    /// </summary>
    public static string Next(string financialYear)
    {
        if (!TryParseStartYear(financialYear, out var startYear))
        {
            throw new ArgumentException($"'{financialYear}' is not a YYYY/YY financial year.", nameof(financialYear));
        }

        return Format(startYear + 1);
    }

    /// <summary>
    /// The financial year before <paramref name="financialYear"/>. Same contract as Next above — the two exist as a
    /// pair so nothing has to shift a year by slicing the string itself.
    /// </summary>
    public static string Previous(string financialYear)
    {
        if (!TryParseStartYear(financialYear, out var startYear))
        {
            throw new ArgumentException($"'{financialYear}' is not a YYYY/YY financial year.", nameof(financialYear));
        }

        return Format(startYear - 1);
    }

    /// <summary>
    /// Whether the string is a well-formed "YYYY/YY" financial year whose end year really is the start year plus one.
    /// "2026/28" is rejected: it would resolve to a window nothing else in the system agrees with.
    /// </summary>
    public static bool IsValid(string? financialYear) => TryParseStartYear(financialYear, out _);

    /// <summary>
    /// The year a "YYYY/YY" financial year starts in ("2026/27" -> 2026), false for anything else. Public so a
    /// caller can ORDER two financial years — the per-activity "Total spent so far" inset carries spend forward
    /// across years (BLUM-7703) and has to know which of two collections is the earlier one. The format lives
    /// here and nowhere else, so nothing may re-derive the start year with its own Substring/Split.
    /// </summary>
    public static bool TryParseStartYear(string? financialYear, out int startYear)
    {
        startYear = 0;

        if (financialYear is null || financialYear.Length != 7 || financialYear[4] != '/')
        {
            return false;
        }

        if (!int.TryParse(financialYear.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(financialYear.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var end))
        {
            return false;
        }

        if (end != (start + 1) % 100)
        {
            return false;
        }

        startYear = start;
        return true;
    }

    private static string Format(int startYear) => $"{startYear}/{(startYear + 1) % 100:D2}";
}