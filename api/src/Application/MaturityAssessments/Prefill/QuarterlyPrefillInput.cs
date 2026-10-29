using Domain.Activities;

namespace Application.MaturityAssessments.Prefill;

// Everything QuarterlyPrefillRules needs about one reportable activity, pre-fetched by the service so the rules
// stay pure. TaskName / CategoryName are the seeded names (ids are minted per environment); KeyResultCodes are the
// standard key results the LA selected on the record; RecordAnswers are the activity-record answers
// (PR_FUNDING_TIME, EDU_WHO_FOR, ...).
public sealed record ActivityFacts(
    Guid ActivityId,
    string TaskName,
    string CategoryName,
    ActivityStatus Status,
    string? CustomTaskText,
    DateTime CreatedAt,
    IReadOnlySet<string> KeyResultCodes,
    IReadOnlyDictionary<string, string> RecordAnswers);

// The answers this activity carried in the organisation's most recent SUBMITTED quarterly report that reported on
// it (BLUM-7856 AC1: drafts never contribute).
public sealed record LatestReport(Guid SubmissionId, DateOnly ReportingPeriodEnd, IReadOnlyDictionary<string, string> Answers);

public sealed record QuarterlyPrefillInput(
    IReadOnlyList<ActivityFacts> Activities,
    IReadOnlyDictionary<Guid, LatestReport> LatestReports,
    DateOnly AssessmentPeriodEnd,
    IReadOnlyDictionary<string, string> WhoForLabels);