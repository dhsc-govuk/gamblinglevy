namespace Application.Common.Validation;

/// <summary>
/// Maximum accepted sizes for user-supplied input, derived from the column widths declared in
/// Infrastructure/Persistence/Configurations.
///
/// A validator limit that is larger than its column — or missing — is not a cosmetic mismatch. The value
/// passes validation, reaches PostgreSQL, raises 22001, and the request 500s; worse, in the bulk-import
/// loops the failed entity stays tracked on the scoped DbContext so every subsequent row fails too. Nine
/// such gaps were live when BLUM-7240 was written, all reachable by an authenticated user with a curl
/// command.
///
/// FieldLimitsTests walks the EF model and fails the build if any column ever narrows below the constant
/// that covers it, which is what stops the list drifting back out of step.
/// </summary>
public static class FieldLimits
{
    public const int Name = 255;              // organisations.name, collections.name, locations.name, activities.title
    public const int Email = 320;             // users.email
    public const int Code = 100;              // form_questions.code, form_modules.code, *.question_code
    public const int Label = 500;             // form_questions.label, form_question_options.label
    public const int OptionValue = 255;       // form_question_options.value
    public const int HelpTextSummary = 500;   // form_questions.help_text_summary
    public const int OnsCode = 64;            // organisations.ons_code
    public const int ECode = 64;              // organisations.e_code, and Organisation.ECodeMaxLength
    public const int FinancialYear = 16;      // collections.financial_year, funding_windows.financial_year
    public const int ShortText = 255;         // varchar(255) free text: activities.custom_task_text, conditional_value
    public const int ClosureReason = 500;     // activities.closure_reason — the protocol's 500-character free-text ceiling

    // 'text' columns. Unbounded in PostgreSQL, so these are a policy choice rather than a schema fact: a
    // request body is capped at 4 MB (Program.cs) and a single field has no reason to fill it.
    public const int Hint = 1000;
    public const int Purpose = 2000;          // collections.purpose
    public const int LongText = 4000;         // descriptions, help text

    // ActivityAnswerValidator.MaxTextLength and SubmissionAnswerValidator.MaxTextLength are defined as this, so a
    // free text answer is bounded identically wherever it is checked. The maturity client mirrors it as
    // MATURITY_ANSWER_MAX_LENGTH, because maturity free text has no protocol limit to catch it first (BLUM-8411).
    public const int AnswerText = 10000;

    // Money. numeric(18,2) holds sixteen integer digits, and MaxMoney matches FundingSpendValidator.MaxAmount
    // so an allocation and a spend entry are bounded the same way.
    public const decimal MaxMoney = 1_000_000_000m;
    public const int MoneyPrecision = 18;
    public const int MoneyScale = 2;

    // Collection bounds. These bound the request rather than a column: an unbounded list is an allocation
    // cost the caller chooses, and for a reorder payload a duplicate silently corrupts display order,
    // which is computed from the list index.
    public const int MaxCollectionItems = 500;
    public const int MaxReminders = 20;

    // A reminder offset is fed to AddDays when the schedule is built, so it needs an upper bound as well as
    // a lower one. Two years is far beyond any collection window.
    public const int MaxReminderDays = 730;

    // A collection's open date and deadline. DateOnly accepts any year up to 9999, so a mistyped 5050 passed
    // validation and was saved (BLUM-8053). Same range as an activity's start year.
    public const int MinDateYear = 2000;
    public const int MaxDateYear = 2100;
    public const int MaxCsvUploadBytes = 10 * 1024 * 1024;

    // An offline quarterly report file (BLUM-8310). A real one is tens of kilobytes; the cap keeps a stray export or
    // a pasted image from being decoded, parsed and validated cell by cell.
    public const int MaxOfflineReportUploadBytes = 5 * 1024 * 1024;

    // Paging. MaxPageSize was previously a silent clamp inside AuditLogRepository; MaxPage exists because
    // (page - 1) * pageSize is computed in an int and would otherwise overflow into a negative OFFSET.
    public const int MinPageSize = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxPage = 100_000;

    // MFA. Generous bounds only — the exact shape is checked inside the handlers so that a malformed code
    // still counts as a failed attempt. See MfaCodeFormat.
    public const int TotpCode = 16;
    public const int RecoveryCode = 32;

    // Audit log filters. audit_logs.action is varchar(64), so a longer filter cannot match anything.
    public const int AuditAction = 64;
}