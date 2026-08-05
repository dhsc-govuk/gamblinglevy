using Api.Dtos;
using Api.Services.Abstract;
using Application.Collections;
using Application.Common.Funding;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Read side of the LA-facing quarterly-report surface. Reads only require the collection to be an assigned
// quarterly collection (a closed report stays viewable); the write commands enforce the open window.
public class QuarterlyReportControllerService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    ISubmissionActivityReportRepository reportRepository,
    ISubmissionActivityAnswerRepository submissionAnswerRepository,
    ISubmissionFeedbackAnswerRepository feedbackAnswerRepository,
    ISubmissionSpendEntryRepository spendRepository,
    IActivityQueries activityQueries,
    IActivityExpectedResultRepository expectedResultRepository,
    IActivityAnswerRepository activityAnswerRepository,
    IKeyResultQueries keyResultQueries,
    IFormModuleQueries formModuleQueries,
    ICategoryQueries categoryQueries,
    IOrganisationQueries organisationQueries,
    IFundingWindowResolver fundingWindowResolver,
    IOrgFundingAllocationRepository orgFundingAllocationRepository) : IQuarterlyReportControllerService
{
    // The context is resolved from the collection id the caller opened — never from "the org's current window".
    // Two quarterly windows can be open at once (a Q2 window still accepting late reports while the Q3 window
    // opens), so any implicit pick would author the report against the wrong quarter (BLUM-6950).
    public async Task<Option<QuarterlyReportContextDto>> GetContextAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        // GetEligibleAsync applies the authoritative, type-aware gate (quarterly + assigned to the caller's org).
        // Like the other reads it deliberately does not require an open window, so a submitted or closed report
        // stays viewable; the write commands are what enforce the window.
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<QuarterlyReportContextDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var organisationId = organisation.Id;

        // A draft can already carry a quarterly Type while its quarter, period and deadline are still unanswered
        // (BLUM-6625 partial drafts), and GetEligibleAsync only gates on Type + assignment. The other reads are
        // shielded by requiring a submission — this one is not, so exclude drafts before the null-forgiving reads.
        if (collection.IsDraft)
        {
            return Option<QuarterlyReportContextDto>.None;
        }

        var submission = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);

        // Option.Match rejects a null result (LanguageExt's null-return check), so the not-yet-created
        // SubmissionId (and the submitted state) is resolved via IsNone/ValueUnsafe instead.
        Guid? submissionId = submission.IsNone ? null : submission.ValueUnsafe().Id.Value;
        DateTime? submittedAt = submission.IsNone ? null : submission.ValueUnsafe().SubmittedAt;
        string? referenceNumber = submission.IsNone ? null : submission.ValueUnsafe().ReferenceNumber;

        // Setup fields are only null while a collection is a draft, and a draft is never assigned to an
        // organisation, so the null-forgiving reads are safe.
        return new QuarterlyReportContextDto(
            collection.Id.Value,
            collection.Name,
            collection.Quarter!.Value,
            collection.FinancialYear!,
            collection.ReportingPeriodStart!.Value,
            collection.ReportingPeriodEnd!.Value,
            collection.SubmissionDeadline!.Value,
            organisation.Name,
            submissionId,
            submittedAt,
            referenceNumber);
    }

    public async Task<Option<SubmissionTaskListDto>> GetTaskListAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<SubmissionTaskListDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var organisationId = organisation.Id;
        var submission = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submission.IsNone)
        {
            return Option<SubmissionTaskListDto>.None;
        }

        return await ToTaskListDtoAsync(submission.ValueUnsafe(), cancellationToken);
    }

    public async Task<SubmissionTaskListDto> ToTaskListDtoAsync(Submission submission, CancellationToken cancellationToken)
    {
        var modules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        var nameById = modules.ToDictionary(m => m.Id, m => m.Name);
        var orderById = modules.ToDictionary(m => m.Id, m => m.DisplayOrder);

        var sections = submission.Sections
            .OrderBy(s => orderById.TryGetValue(s.FormModuleId, out var order) ? order : int.MaxValue)
            .Select(s => new SubmissionSectionDto(
                s.ModuleCode,
                nameById.TryGetValue(s.FormModuleId, out var name) ? name : s.ModuleCode,
                s.Status,
                s.ReviewRequired,
                IsAvailable: submission.AreActivitiesConfirmed))
            .ToList();

        // Submit gate: activities confirmed, nothing flagged for review, every section Completed, not already submitted.
        var canSubmit = submission.AreActivitiesConfirmed
            && !submission.ActivitiesReviewRequired
            && !submission.IsSubmitted
            && submission.AreAllSectionsCompleted;

        return new SubmissionTaskListDto(
            submission.Id.Value,
            submission.AreActivitiesConfirmed,
            submission.ActivitiesReviewRequired,
            sections,
            canSubmit,
            submission.IsSubmitted,
            submission.ReferenceNumber);
    }

    public async Task<Option<IReadOnlyList<ReportableActivityDto>>> GetReportableActivitiesAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<IReadOnlyList<ReportableActivityDto>>.None;
        }

        var (collection, organisation) = eligible.Value;
        var organisationId = organisation.Id;
        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<IReadOnlyList<ReportableActivityDto>>.None;
        }

        var submission = submissionLookup.ValueUnsafe();
        var activities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);
        var reportable = activities.Where(ActivityReportContextResolver.IsReportable).ToList();

        var reports = await reportRepository.GetForSubmissionAsync(submission.Id, cancellationToken);
        var statusByActivity = reports.ToDictionary(r => r.ActivityId, r => r.Status.ToString());
        var taskNames = await GetTaskNamesAsync(cancellationToken);

        IReadOnlyList<ReportableActivityDto> rows = reportable
            .Select(a =>
            {
                var taskName = ResolveName(taskNames, a.TaskId?.Value);
                return new ReportableActivityDto(
                    a.Id.Value,
                    ResolveDisplayTitle(a, taskName),
                    taskName,
                    a.Status!.ToString(),
                    statusByActivity.TryGetValue(a.Id, out var reportStatus) ? reportStatus : SubmissionSectionStatuses.NotStarted);
            })
            .ToList();

        return Option<IReadOnlyList<ReportableActivityDto>>.Some(rows);
    }

    public async Task<Option<ActivityReportQuestionsDto>> GetActivityQuestionsAsync(
        Permission permission,
        Guid collectionId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<ActivityReportQuestionsDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var organisationId = organisation.Id;
        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<ActivityReportQuestionsDto>.None;
        }

        var submission = submissionLookup.ValueUnsafe();
        var activityLookup = await activityQueries.GetByIdForOrgAsync(new ActivityId(activityId), organisationId, cancellationToken: cancellationToken);
        if (activityLookup.IsNone)
        {
            return Option<ActivityReportQuestionsDto>.None;
        }

        var activity = activityLookup.ValueUnsafe();
        if (!ActivityReportContextResolver.IsReportable(activity))
        {
            return Option<ActivityReportQuestionsDto>.None;
        }

        // Resolve the question set the same way the save command does, then merge values: prefill from the
        // activity record first, saved report answers on top.
        var expectedResults = await expectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var questionBank = activity.TaskId is null
            ? []
            : await keyResultQueries.GetQuestionsForTaskAsync(activity.TaskId, cancellationToken);
        var deliveryQuestions = await GetDeliveryQuestionsAsync(cancellationToken);
        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, questionBank, deliveryQuestions);

        var activityAnswers = await activityAnswerRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var values = new Dictionary<string, string?>(KeyResultQuestionResolver.BuildPrefill(resolved, activityAnswers), StringComparer.Ordinal);

        var savedAnswers = await submissionAnswerRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        foreach (var answer in savedAnswers)
        {
            values[answer.QuestionCode] = answer.Value;
        }

        var report = await reportRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        var taskNames = await GetTaskNamesAsync(cancellationToken);
        var taskName = ResolveName(taskNames, activity.TaskId?.Value);

        return new ActivityReportQuestionsDto(
            activity.Id.Value,
            ResolveDisplayTitle(activity, taskName),
            activity.Status!.ToString(),
            report.Match(r => r.Status.ToString(), () => SubmissionSectionStatuses.NotStarted),
            resolved.Select(ToQuestionDto).ToList(),
            values.Select(v => new ActivityAnswerDto(v.Key, v.Value)).ToList());
    }

    public async Task<Option<FundingSpendDto>> GetFundingSpendAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<FundingSpendDto>.None;
        }

        var (collection, organisation) = eligible.Value;

        // Same guard as GetContextAsync, for the same null-forgiving reads below. Requiring a submission is NOT
        // enough on its own: a submission cannot be created against a draft, but an admin can revert a PUBLISHED
        // collection to draft after reports exist and then clear its quarter/financial year, at which point
        // `collection.Quarter!.Value` throws and the LA gets an unhandled 500 (BLUM-7216 review).
        if (collection.IsDraft)
        {
            return Option<FundingSpendDto>.None;
        }

        var organisationId = organisation.Id;
        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<FundingSpendDto>.None;
        }

        var submission = submissionLookup.ValueUnsafe();
        var currentQuarter = collection.Quarter!.Value;
        var financialYear = collection.FinancialYear!;
        var remaining = ReportingPeriod.RemainingQuarters(currentQuarter);
        var remainingDtos = remaining
            .Select(q =>
            {
                var (start, end) = ReportingPeriod.Derive(financialYear, q);
                return new FundingSpendQuarterDto(q, start, end);
            })
            .ToList();

        var activities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);
        var reportable = activities.Where(FundingSpendContextResolver.IsFundingReportable).ToList();

        // Saved spend for this submission, split into the two screens.
        var entries = await spendRepository.GetForSubmissionAsync(submission.Id, cancellationToken);
        var actualByActivity = new Dictionary<Guid, decimal>();
        decimal? otherActual = null;
        var forecastByActivityQuarter = new Dictionary<(Guid ActivityId, int Quarter), decimal>();
        var otherForecast = new Dictionary<int, decimal?>();
        foreach (var entry in entries)
        {
            if (entry.Kind.IsActual)
            {
                if (entry.ActivityId is null)
                {
                    otherActual = entry.Amount;
                }
                else
                {
                    actualByActivity[entry.ActivityId.Value] = entry.Amount;
                }
            }
            else if (entry.Quarter is int quarter)
            {
                if (entry.ActivityId is null)
                {
                    otherForecast[quarter] = entry.Amount;
                }
                else
                {
                    forecastByActivityQuarter[(entry.ActivityId.Value, quarter)] = entry.Amount;
                }
            }
        }

        // "Spend to date" / per-activity "Total spent so far" = accumulated actual spend in earlier quarters of the
        // same financial year. No prior submissions (the first quarter) => £0.
        var priorSubmissions = (await submissionQueries.GetForOrgAndFinancialYearAsync(organisationId, financialYear, cancellationToken))
            .Where(s => s.Quarter is int quarter && quarter < currentQuarter && s.SubmissionId != submission.Id)
            .Select(s => s.SubmissionId)
            .ToList();
        var priorActualByActivity = new Dictionary<Guid, decimal>();
        var spendToDate = 0m;
        foreach (var priorSubmissionId in priorSubmissions)
        {
            var priorEntries = await spendRepository.GetForSubmissionAsync(priorSubmissionId, cancellationToken);
            foreach (var priorEntry in priorEntries.Where(e => e.Kind.IsActual))
            {
                spendToDate += priorEntry.Amount;
                if (priorEntry.ActivityId is { } activityId)
                {
                    priorActualByActivity[activityId.Value] = priorActualByActivity.GetValueOrDefault(activityId.Value) + priorEntry.Amount;
                }
            }
        }

        var taskNames = await GetTaskNamesAsync(cancellationToken);
        var activityDtos = reportable
            .Select(a =>
            {
                var taskName = ResolveName(taskNames, a.TaskId?.Value);
                var forecast = remaining.ToDictionary(
                    q => q,
                    q => forecastByActivityQuarter.TryGetValue((a.Id.Value, q), out var value) ? value : (decimal?)null);
                return new FundingSpendActivityDto(
                    a.Id.Value,
                    ResolveDisplayTitle(a, taskName),
                    taskName,
                    actualByActivity.TryGetValue(a.Id.Value, out var actual) ? actual : null,
                    priorActualByActivity.GetValueOrDefault(a.Id.Value),
                    forecast);
            })
            .ToList();

        var otherForecastDto = remaining.ToDictionary(
            q => q,
            q => otherForecast.TryGetValue(q, out var value) ? value : (decimal?)null);

        // "Total grant allocation" = the org's allocation for the current funding window (0 if none).
        var window = await fundingWindowResolver.ResolveCurrentAsync(cancellationToken);
        var allocation = await orgFundingAllocationRepository.FindForOrganisationAndWindowAsync(organisationId, window.Id, cancellationToken);
        var grantAllocation = allocation.Match(a => a.AllocatedAmount, () => 0m);

        // Sum the figures actually shown on the screen (reportable activities + the "other" lines, over the
        // remaining quarters) so the summary box always reconciles with its line items — never the raw entry set,
        // which could include rows for now-unreportable activities or quarters outside the current remaining set.
        var totalSpendThisQuarter = activityDtos.Sum(a => a.ActualThisQuarter ?? 0m) + (otherActual ?? 0m);

        // "Expected spend for the year" is the full financial year: actual spent in earlier quarters (spendToDate)
        // plus actual spent this quarter plus the forecast for the remaining (future) quarters. The reporting
        // quarter is no longer in the forecast set, so it must be added via totalSpendThisQuarter — otherwise the
        // year total would silently drop the quarter being reported (and would be just spendToDate + actual in Q4,
        // where there is nothing left to forecast).
        var totalForecast = activityDtos.Sum(a => a.Forecast.Values.Sum(v => v ?? 0m)) + otherForecastDto.Values.Sum(v => v ?? 0m);
        var expectedSpendForYear = spendToDate + totalSpendThisQuarter + totalForecast;

        var summary = new FundingSpendSummaryDto(grantAllocation, totalSpendThisQuarter, expectedSpendForYear, spendToDate);

        var sectionStatus = submission.Sections
            .FirstOrDefault(s => s.ModuleCode == SubmissionModules.FundingSpent)?.Status.ToString()
            ?? SubmissionSectionStatuses.NotStarted;

        return new FundingSpendDto(
            submission.Id.Value,
            currentQuarter,
            financialYear,
            sectionStatus,
            remainingDtos,
            activityDtos,
            otherActual,
            otherForecastDto,
            summary);
    }

    public async Task<Option<StrategicFeedbackDto>> GetStrategicFeedbackAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<StrategicFeedbackDto>.None;
        }

        var (collection, organisation) = eligible.Value;

        // See GetFundingSpendAsync: a reverted-to-draft collection can have a null Quarter/FinancialYear while
        // still carrying submissions, and this method null-forgives both when building the DTO.
        if (collection.IsDraft)
        {
            return Option<StrategicFeedbackDto>.None;
        }

        var organisationId = organisation.Id;
        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return Option<StrategicFeedbackDto>.None;
        }

        var submission = submissionLookup.ValueUnsafe();

        var moduleLookup = await formModuleQueries.GetByCodeAsync(SubmissionModules.StrategicFeedback, cancellationToken);
        if (moduleLookup.IsNone)
        {
            return Option<StrategicFeedbackDto>.None;
        }

        var withChildren = await formModuleQueries.GetByIdWithChildrenAsync(moduleLookup.ValueUnsafe().Id, cancellationToken);
        if (withChildren.IsNone)
        {
            return Option<StrategicFeedbackDto>.None;
        }

        var module = withChildren.ValueUnsafe();

        // Each form section is one page; the question's Section slug (derived from the stable section number, not the
        // title text) is what the frontend filters on to render each page and its check-your-answers card.
        var pages = module.Sections
            .Where(s => s.IsActive)
            .OrderBy(s => s.SectionNumber)
            .Select(section =>
            {
                var slug = FeedbackSectionSlug(section.SectionNumber);
                var questions = module.Questions
                    .Where(q => q.IsActive && q.SectionId == section.Id)
                    .OrderBy(q => q.DisplayOrder)
                    .Select(q => ToFeedbackQuestionDto(q, slug))
                    .ToList();
                return new StrategicFeedbackPageDto(slug, section.Title, questions);
            })
            .Where(p => p.Questions.Count > 0)
            .ToList();

        var savedAnswers = await feedbackAnswerRepository.GetForSubmissionAsync(submission.Id, cancellationToken);
        var answers = savedAnswers
            .Select(a => new StrategicFeedbackAnswerDto(a.QuestionCode, a.Value))
            .ToList();

        var sectionStatus = submission.Sections
            .FirstOrDefault(s => s.ModuleCode == SubmissionModules.StrategicFeedback)?.Status.ToString()
            ?? SubmissionSectionStatuses.NotStarted;

        return new StrategicFeedbackDto(
            submission.Id.Value,
            collection.Quarter!.Value,
            collection.FinancialYear!,
            sectionStatus,
            pages,
            answers);
    }

    // The page slug for a feedback form section. Decoupled from the title wording so a reword never breaks the
    // frontend page filter / testids.
    private static string FeedbackSectionSlug(int sectionNumber) => sectionNumber switch
    {
        1 => "wider-progress",
        2 => "working-together",
        _ => $"section-{sectionNumber}",
    };

    private static ReportQuestionDto ToFeedbackQuestionDto(Domain.DataCollectionForms.DcFormQuestion question, string sectionSlug) => new(
        question.Id.Value,
        question.Code,
        question.Label,
        question.Hint,
        question.QuestionType.ToString(),
        question.IsRequired,
        question.DisplayOrder,
        null,
        null,
        question.Options
            .Where(option => option.IsActive)
            .OrderBy(option => option.DisplayOrder)
            .Select(option => new ReportQuestionOptionDto(option.Value, option.Label))
            .ToList(),
        null,
        null,
        null,
        sectionSlug);

    private static ReportQuestionDto ToQuestionDto(ResolvedQuestion question) => new(
        question.Id,
        question.Code,
        question.Label,
        question.Hint,
        question.QuestionType,
        question.IsRequired,
        question.DisplayOrder,
        question.ConditionalQuestionCode,
        question.ConditionalValue,
        question.Options.Select(o => new ReportQuestionOptionDto(o.Value, o.Label)).ToList(),
        question.MinValue,
        question.MaxValue,
        question.IsPercentage ? "%" : null,
        question.Section);

    // Returns the resolved Organisation (not just its id) so callers that need its name — the report context —
    // do not have to load it a second time.
    private async Task<(Collection Collection, Organisation Organisation)?> GetEligibleAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return null;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return null;
        }

        var organisation = organisationLookup.ValueUnsafe();

        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), cancellationToken: cancellationToken);
        if (lookup.IsNone)
        {
            return null;
        }

        var collection = lookup.ValueUnsafe();
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return null;
        }

        return (collection, organisation);
    }

    private async Task<IReadOnlyList<Domain.DataCollectionForms.DcFormQuestion>> GetDeliveryQuestionsAsync(
        CancellationToken cancellationToken)
    {
        var moduleLookup = await formModuleQueries.GetByCodeAsync(SubmissionModules.ActivityKeyResults, cancellationToken);
        if (moduleLookup.IsNone)
        {
            return [];
        }

        var withChildren = await formModuleQueries.GetByIdWithChildrenAsync(moduleLookup.ValueUnsafe().Id, cancellationToken);
        return withChildren.IsNone ? [] : withChildren.ValueUnsafe().Questions.ToList();
    }

    // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see ActivitiesControllerService.ResolveOrg),
    // the same reason GetContextAsync above resolves its nullable submission fields this way.
    private static OrganisationId? ResolveOrg(Permission permission) =>
        permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();

    // Mirrors the activities list fallback: name, then the custom "Other" text, then the type name.
    private static string ResolveDisplayTitle(Activity activity, string? taskName)
    {
        if (!string.IsNullOrWhiteSpace(activity.Title))
        {
            return activity.Title!;
        }

        if (!string.IsNullOrWhiteSpace(activity.CustomTaskText))
        {
            return activity.CustomTaskText!;
        }

        return string.IsNullOrWhiteSpace(taskName) ? "Untitled activity" : taskName!;
    }

    private async Task<Dictionary<Guid, string>> GetTaskNamesAsync(CancellationToken cancellationToken)
    {
        var tasks = await categoryQueries.GetActiveTasksAsync(cancellationToken);
        return tasks.ToDictionary(t => t.Id.Value, t => t.TaskName);
    }

    private static string? ResolveName(IReadOnlyDictionary<Guid, string> lookup, Guid? id)
    {
        if (id is null)
        {
            return null;
        }

        return lookup.TryGetValue(id.Value, out var name) ? name : null;
    }
}