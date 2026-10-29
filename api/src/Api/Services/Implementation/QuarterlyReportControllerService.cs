using Api.Dtos;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
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
    CollectionFormModules collectionFormModules,
    ICategoryQueries categoryQueries,
    IOrganisationQueries organisationQueries,
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
        var deliveryQuestions = await ActivityReportContextResolver.LoadDeliveryQuestionsAsync(collectionFormModules, collection, cancellationToken);
        var isJointPartnership = await ActivityReportContextResolver.IsJointPartnershipAsync(categoryQueries, activity, cancellationToken);
        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, questionBank, deliveryQuestions, isJointPartnership);

        var activityAnswers = await activityAnswerRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var savedAnswers = await submissionAnswerRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        var values = KeyResultQuestionResolver.InitialValues(resolved, activityAnswers, savedAnswers);

        var report = await reportRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        var taskNames = await GetTaskNamesAsync(cancellationToken);
        var taskName = ResolveName(taskNames, activity.TaskId?.Value);

        return new ActivityReportQuestionsDto(
            activity.Id.Value,
            ResolveDisplayTitle(activity, taskName),
            activity.Status!.ToString(),
            report.Match(r => r.Status.ToString(), () => SubmissionSectionStatuses.NotStarted),
            resolved.Select(ReportQuestionMapper.ToDto).ToList(),
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

        // One row per activity in this quarter's funding section (BLUM-7672): the actual-spend set, plus Planned
        // activities as forecast-only rows. actualSpendIds stays separate because THIS quarter's actual figures are
        // scoped to the "Activity spending" screen, which a Planned activity is never on — see the scoping note on
        // actualByKeyAndQuarter below.
        //
        // Q4 keeps the Planned rows too, although it has no forecast quarters left to fill in. The row is where the
        // export reads an activity's EARLIER forecasts from (ForecastByQuarter, the Q{n} Forecast columns): without it
        // the Q2-Q4 forecasts an LA recorded against a still-Planned activity in its Q1-Q3 reports vanish from the Q4
        // export, while an Active activity's stay — and any earlier actuals it carries land on a "(no longer
        // reported)" row for an activity that is live. The LA screens are unaffected: Activity spending leaves
        // forecast-only rows out, and a Q4 report has no Forecast spend screen.
        var actualSpend = activities.Where(FundingSpendContextResolver.IsActualSpendReportable).ToList();
        var rows = activities.Where(FundingSpendContextResolver.IsForecastSpendReportable).ToList();
        var actualSpendIds = actualSpend.Select(a => a.Id.Value).ToHashSet();

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

        // "Spend to date" = accumulated actual spend in earlier quarters of the same financial year. No prior
        // submissions (the first quarter) => £0. The per-activity "Total spent so far" inset starts here too but
        // does NOT stop here — the earlier-YEARS pass below widens it (BLUM-7703).
        //
        // Strictly EARLIER quarters: a submission sharing this report's own quarter (an organisation assigned to two
        // live collections for the same quarter) is another report's answer, not this one's. Folding it in would make
        // this report's per-activity cells the sum of both, so summing the rows across a window double-counted it and
        // a row's ActualByQuarter disagreed with its own ActualThisQuarter.
        var priorSubmissionRows = (await submissionQueries.GetForOrgAndFinancialYearAsync(organisationId, financialYear, cancellationToken))
            .Where(s => s.Quarter is int quarter && quarter < currentQuarter && s.SubmissionId != submission.Id)
            .OrderBy(s => s.Quarter)
            .ToList();
        var priorActualByActivity = new Dictionary<Guid, decimal>();
        var spendToDate = 0m;

        // Export-only aggregates (BLUM-7130 CSV review): actual spend bucketed by the quarter it was actually
        // reported in, and the MOST RECENTLY RECORDED forecast for each target quarter — a future quarter can be
        // forecast by more than one earlier submission (e.g. Q1's report forecasts Q3, then Q2's report re-forecasts
        // Q3), and DHSC confirmed the latest estimate should win. Iterating prior submissions in ascending quarter
        // order and folding in the current submission last makes "last write wins" exactly implement that rule.
        //
        // actualByKeyAndQuarter is the single source of truth for actual spend (BLUM-7607) — both the quarterly
        // columns and the organisation total derive from it, which is what makes them reconcile. It is keyed by
        // activity id (null = "Other"), and the two quarter ranges are deliberately scoped differently:
        //
        //   EARLIER quarters take EVERY recorded entry, reportable or not. Those amounts were confirmed and frozen
        //   into a report that has already been submitted, so dropping the ones whose activity has since been deleted
        //   or reverted to Planned is what left the quarterly columns unable to add up to the total.
        //
        //   THIS quarter takes only what the "Activity spending" screen shows — actual-spend activities plus the "Other"
        //   line, exactly the set totalSpendThisQuarter sums. A Planned activity's forecast-only row is not on that
        //   screen, so an Actual row still sitting against it (saved before it was reverted to Planned) stays out too. The snapshot has to freeze the figures the LA actually
        //   checked and confirmed, and an amount recorded against an activity they then deleted appears in no row on
        //   that screen. It is also transient: the next save of that screen rewrites the Actual rows from the
        //   reportable set and genuinely deletes it. Scoping it this way is what keeps
        //   totalSpendToDate == spendToDate + totalSpendThisQuarter, so the summary box and the CSV agree.
        var actualByKeyAndQuarter = new Dictionary<(Guid? ActivityId, int Quarter), decimal>();
        var forecastByKeyAndTargetQuarter = new Dictionary<(Guid? ActivityId, int TargetQuarter), decimal>();

        foreach (var priorSubmissionRow in priorSubmissionRows)
        {
            var priorQuarter = priorSubmissionRow.Quarter!.Value;
            var priorEntries = await spendRepository.GetForSubmissionAsync(priorSubmissionRow.SubmissionId, cancellationToken);
            foreach (var priorEntry in priorEntries.Where(e => e.Kind.IsActual))
            {
                spendToDate += priorEntry.Amount;
                if (priorEntry.ActivityId is { } activityId)
                {
                    priorActualByActivity[activityId.Value] = priorActualByActivity.GetValueOrDefault(activityId.Value) + priorEntry.Amount;
                }

                var actualKey = (priorEntry.ActivityId?.Value, priorQuarter);
                actualByKeyAndQuarter[actualKey] = actualByKeyAndQuarter.GetValueOrDefault(actualKey) + priorEntry.Amount;
            }

            foreach (var priorForecastEntry in priorEntries.Where(e => e.Kind.IsForecast && e.Quarter is int))
            {
                var forecastKey = (priorForecastEntry.ActivityId?.Value, priorForecastEntry.Quarter!.Value);
                forecastByKeyAndTargetQuarter[forecastKey] = priorForecastEntry.Amount;
            }
        }

        // BLUM-7703: the inset reads "Total spent so far" on THIS ACTIVITY, not "spent so far this year". An activity
        // outlives a financial year, so spend recorded against it in 2025/26 must still show when the LA reports in
        // 2026/27 — before this, the first report of a new year showed £0 on every activity, however much had been
        // reported against them.
        //
        // Only priorActualByActivity widens. spendToDate, actualByKeyAndQuarter and forecastByKeyAndTargetQuarter above
        // stay year-scoped, because everything they feed (the summary box, remaining budget, the export's Q1-Q4 columns)
        // measures this report against this report's own annual allocation. That is why this is a separate pass rather
        // than a wider query feeding the loop above.
        //
        // Strictly EARLIER years keeps these rows disjoint from the same-year ones already folded in above, so the two
        // passes cannot double-count each other. Within an earlier year the rows are taken as recorded — two live
        // collections sharing a quarter both count, exactly as they already do for an earlier quarter of this year.
        // A row whose year is missing or malformed is skipped rather than guessed at.
        //
        // Skipped entirely when nothing reportable will read the dictionary, which is also the whole "Record funding
        // spent" section being empty.
        if (rows.Count > 0 && FinancialYearRange.TryParseStartYear(financialYear, out var currentStartYear))
        {
            var recordedSpend = await spendRepository.GetActualActivitySpendByFinancialYearAsync(organisationId, cancellationToken);
            foreach (var row in recordedSpend)
            {
                if (!FinancialYearRange.TryParseStartYear(row.FinancialYear, out var rowStartYear) || rowStartYear >= currentStartYear)
                {
                    continue;
                }

                priorActualByActivity[row.ActivityId] = priorActualByActivity.GetValueOrDefault(row.ActivityId) + row.Amount;
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Kind.IsActual)
            {
                // The "Other" line (null activity) always counts; an activity's amount counts only while it is still
                // on the "Activity spending" screen — see the scoping note above.
                if (entry.ActivityId is { } entryActivityId && !actualSpendIds.Contains(entryActivityId.Value))
                {
                    continue;
                }

                var actualKey = (entry.ActivityId?.Value, currentQuarter);
                actualByKeyAndQuarter[actualKey] = actualByKeyAndQuarter.GetValueOrDefault(actualKey) + entry.Amount;
            }
            else if (entry.Quarter is int quarter)
            {
                var forecastKey = (entry.ActivityId?.Value, quarter);
                forecastByKeyAndTargetQuarter[forecastKey] = entry.Amount;
            }
        }

        // The organisation's actual spend per quarter, over EVERY recorded entry — reportable activities, activities
        // that are no longer reportable, and the "Other" line alike. This is what the export's Q{n} total actual
        // columns and Total spend to date are BOTH derived from, so they cannot disagree (BLUM-7607). Blank (null)
        // means "no entry exists for that quarter", never "confirmed zero" — a quarter whose entries genuinely sum to
        // zero still renders 0.00. Nothing can key a quarter later than the reporting one, so that guard is only
        // belt-and-braces.
        var totalActualByQuarter = Enumerable.Range(1, 4).ToDictionary(
            q => q,
            q =>
            {
                if (q > currentQuarter)
                {
                    return (decimal?)null;
                }

                var forQuarter = actualByKeyAndQuarter.Where(kv => kv.Key.Quarter == q).ToList();
                return forQuarter.Count == 0 ? null : forQuarter.Sum(kv => kv.Value);
            });

        var taskNames = await GetTaskNamesAsync(cancellationToken);
        var categoryNames = await GetCategoryNamesAsync(cancellationToken);
        var activityDtos = rows
            .Select(a =>
            {
                var taskName = ResolveName(taskNames, a.TaskId?.Value);
                var forecastOnly = !actualSpendIds.Contains(a.Id.Value);
                var forecast = remaining.ToDictionary(
                    q => q,
                    q => forecastByActivityQuarter.TryGetValue((a.Id.Value, q), out var value) ? value : (decimal?)null);

                var actualByQuarter = Enumerable.Range(1, 4).ToDictionary(
                    q => q,
                    q => actualByKeyAndQuarter.TryGetValue((a.Id.Value, q), out var value) ? (decimal?)value : null);
                var forecastByQuarter = Enumerable.Range(2, 3).ToDictionary(
                    q => q,
                    q => forecastByKeyAndTargetQuarter.TryGetValue((a.Id.Value, q), out var value) ? (decimal?)value : null);

                // Null for a forecast-only row whatever is saved: it has no input on "Activity spending", so an Actual
                // row against it is a stale leftover the next actuals save deletes, and must not reach the totals.
                var actualThisQuarter = !forecastOnly && actualByActivity.TryGetValue(a.Id.Value, out var actual) ? (decimal?)actual : null;
                // Every earlier quarter this activity has been reported in, including earlier financial years
                // (BLUM-7703) — deliberately a different question from the year-scoped Summary.SpendToDate beside it.
                var totalSpentSoFar = priorActualByActivity.GetValueOrDefault(a.Id.Value);

                // Summed off the same per-quarter bucket the ActualByQuarter cells come from, so a row's
                // "Total actual to date" always equals its own Q1-Q4 cells (BLUM-7607). This is THIS financial year up
                // to and including the reporting quarter — narrower than TotalSpentSoFar above, which spans every year
                // but stops before this quarter.
                var totalActualToDate = SumToDate(actualByQuarter, currentQuarter);

                // forecastByQuarter carries the historical Q2-4 record regardless of whether a quarter has since
                // been actualised — Projected spend must only add the quarters still genuinely ahead of this
                // report, or an already-actualised quarter's old forecast gets summed on top of its own actual
                // (already inside totalActualToDate), inflating the projection.
                var projectedActivitySpend = totalActualToDate
                    + remaining.Sum(q => forecastByQuarter.TryGetValue(q, out var v) ? v ?? 0m : 0m);

                return new FundingSpendActivityDto(
                    a.Id.Value,
                    ResolveDisplayTitle(a, taskName),
                    taskName,
                    actualThisQuarter,
                    totalSpentSoFar,
                    forecast,
                    ResolveName(categoryNames, a.CategoryId?.Value),
                    actualByQuarter,
                    forecastByQuarter,
                    totalActualToDate,
                    projectedActivitySpend,
                    forecastOnly);
            })
            .ToList();

        var otherForecastDto = remaining.ToDictionary(
            q => q,
            q => otherForecast.TryGetValue(q, out var value) ? value : (decimal?)null);

        var otherActualByQuarter = Enumerable.Range(1, 4).ToDictionary(
            q => q,
            q => actualByKeyAndQuarter.TryGetValue((null, q), out var value) ? (decimal?)value : null);
        var otherForecastByQuarter = Enumerable.Range(2, 3).ToDictionary(
            q => q,
            q => forecastByKeyAndTargetQuarter.TryGetValue((null, q), out var value) ? (decimal?)value : null);
        var otherTotalActualToDate = SumToDate(otherActualByQuarter, currentQuarter);
        var otherProjectedSpend = otherTotalActualToDate
            + remaining.Sum(q => otherForecastByQuarter.TryGetValue(q, out var v) ? v ?? 0m : 0m);

        // "Total grant allocation" is versioned per financial year (BLUM-7608), so this report is measured
        // against the allocation for the COLLECTION's own year rather than whichever row the organisation happens
        // to hold most recently — otherwise, once DHSC records next year's figure, every Q1-Q4 report of the year
        // in progress silently starts comparing spend against next year's money.
        //
        // AllocationSelector's "exact year, else the latest earlier year, else the earliest later year" order is
        // load-bearing, not defensive. Before this ticket an organisation held exactly ONE allocation row,
        // windowed to whatever FY was current at onboarding or import time, and every amendment updated that same
        // row in place — the shape the bulk import still produces. A bare year-scoped lookup would match only for
        // a collection whose year happens to equal the org's onboarding year, silently zeroing the allocation for
        // every existing report; and preferring a LATER year would measure a 2025/26 report against a 2027/28
        // allocation nobody had agreed when it was authored.
        //
        // The upcoming year's figure is export-only (AC3): it names money on the CSV beside this year's, and
        // deliberately does NOT enter remainingBudget or projectedVariance, which measure this report's year
        // against this report's money.
        var held = await orgFundingAllocationRepository.ListWithWindowsForOrganisationsAsync([organisationId], cancellationToken);
        var (allocation, nextYearAllocation) = AllocationSelector.SelectForFinancialYear(held, financialYear);
        var grantAllocationFinancialYear = allocation?.Window.FinancialYear;
        var grantAllocation = allocation?.Allocation.AllocatedAmount ?? 0m;

        // Export-only (BLUM-7608 AC3): the upcoming year's allocation rides along so funding-summary.csv can name
        // both figures and both years. Deliberately NOT folded into remainingBudget or projectedVariance - those
        // measure this report's year against this report's money.
        // Sum the figures actually shown on the screens (the activity rows + the "other" lines, over the remaining
        // quarters) so the summary box always reconciles with its line items — never the raw entry set, which could
        // include rows for now-unreportable activities or quarters outside the current remaining set. Forecast-only
        // (Planned) rows fall out of this quarter's actual by their null ActualThisQuarter and into the forecast by
        // their Forecast cells, which is exactly the screen each figure comes from (BLUM-7672).
        var totalSpendThisQuarter = activityDtos.Sum(a => a.ActualThisQuarter ?? 0m) + (otherActual ?? 0m);

        var totalForecast = activityDtos.Sum(a => a.Forecast.Values.Sum(v => v ?? 0m)) + otherForecastDto.Values.Sum(v => v ?? 0m);

        // Export-only totals (BLUM-7130 CSV review), all derived from totalActualByQuarter so the CSV's quarterly
        // columns add up to its total by construction (BLUM-7607). This used to call ISpendReportTotals, which counts
        // every spend row while the quarterly columns counted only reportable activities — so spend against an
        // activity since deleted, reverted to Planned or left incomplete sat in the total and in no column. That
        // roll-up is still what the LA Funding Dashboard shows; only this call site derives its own.
        //
        // By the scoping above this is exactly spendToDate + totalSpendThisQuarter, so the LA's summary box reconciles
        // with it too. Quarters later than the reporting one cannot reach the bucket, so a later quarter's
        // already-drafted actuals still cannot leak backward into this report's frozen figure.
        var totalSpendToDate = totalActualByQuarter.Values.Sum(v => v ?? 0m);
        var remainingBudget = grantAllocation - totalSpendToDate;

        // "Expected spend for the year" is the full financial year: everything actually spent up to and including the
        // reporting quarter, plus the forecast for the quarters still ahead of it. The reporting quarter is not in the
        // forecast set (it is an actual), so in Q4 this is just the spend to date, with nothing left to forecast.
        //
        // Stated as totalSpendToDate + totalForecast rather than spendToDate + totalSpendThisQuarter + totalForecast:
        // the two are equal, and writing it this way makes the identity the CSV depends on
        // (Forecasted year-end = Total spend to date + Total Forecast) visible at its source.
        var expectedSpendForYear = totalSpendToDate + totalForecast;
        var projectedVariance = grantAllocation - expectedSpendForYear;

        var summary = new FundingSpendSummaryDto(
            grantAllocation,
            totalSpendThisQuarter,
            expectedSpendForYear,
            spendToDate,
            totalSpendToDate,
            remainingBudget,
            totalForecast,
            projectedVariance,
            grantAllocationFinancialYear,
            nextYearAllocation?.Allocation.AllocatedAmount,
            nextYearAllocation?.Window.FinancialYear);

        var unreportedActivities = await BuildUnreportedActivitiesAsync(
            organisationId, activities, rows, actualByKeyAndQuarter, currentQuarter, taskNames, categoryNames, cancellationToken);

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
            otherActualByQuarter,
            otherForecastByQuarter,
            otherTotalActualToDate,
            otherProjectedSpend,
            summary,
            totalActualByQuarter,
            unreportedActivities);
    }

    // Actual spend recorded against an activity that has no row of its own in this report — soft-deleted, or whose
    // setup is no longer complete (a Planned activity keeps a forecast-only row, which carries its earlier actuals
    // itself — BLUM-7672). Those amounts are real: they were recorded, submitted and frozen into an earlier quarter's
    // report, and dropping them is what left the export's quarterly columns unable to add up to its own total
    // (BLUM-7607). They are surfaced as their own rows rather than folded into the activity rows so DHSC can see where
    // the money went. Keying this on "has no row", not on a status, is what keeps every actual in exactly one row
    // whichever set the rows were built from.
    //
    // `activities` already holds every non-deleted record for the organisation (drafts and Planned included), so the
    // soft-deleted set is the only extra read — taken lazily, since most organisations have nothing orphaned.
    private async Task<IReadOnlyList<FundingSpendUnreportedActivityDto>> BuildUnreportedActivitiesAsync(
        OrganisationId organisationId,
        IReadOnlyList<Activity> activities,
        IReadOnlyList<Activity> activityRows,
        IReadOnlyDictionary<(Guid? ActivityId, int Quarter), decimal> actualByKeyAndQuarter,
        int currentQuarter,
        Dictionary<Guid, string> taskNames,
        Dictionary<Guid, string> categoryNames,
        CancellationToken cancellationToken)
    {
        // Keyed on entries that actually carry money. "£0" is a valid answer the GDS copy asks for explicitly, and
        // Complete requires an amount for every actual-spend activity, so most organisations have £0 rows for work they
        // did not spend on this quarter. Without this an activity that recorded £0 and was later deleted would leave a
        // permanent all-zero "(no longer reported)" row in every future quarter's export — steady noise, and a row
        // summing to zero reconciles nothing.
        var rowIds = activityRows.Select(a => a.Id.Value).ToHashSet();
        var unreportedIds = actualByKeyAndQuarter
            .Where(kv => kv.Key.ActivityId is { } id && !rowIds.Contains(id) && kv.Value != 0m)
            .Select(kv => kv.Key.ActivityId!.Value)
            .ToHashSet();

        if (unreportedIds.Count == 0)
        {
            return [];
        }

        var deleted = await activityQueries.GetDeletedByOrganisationAsync(organisationId, cancellationToken);
        var byId = activities.Concat(deleted)
            .GroupBy(a => a.Id.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<FundingSpendUnreportedActivityDto>();
        var unresolvedByQuarter = new Dictionary<int, decimal?>();

        foreach (var activityId in unreportedIds)
        {
            var actualByQuarter = QuarterMap(q => actualByKeyAndQuarter.TryGetValue((activityId, q), out var v) ? v : (decimal?)null);

            if (!byId.TryGetValue(activityId, out var activity))
            {
                // A spend row references its activity by id with no foreign key, so an id that resolves to nothing at
                // all is possible. Its money still has to appear somewhere or the total stops reconciling — it folds
                // into one catch-all row rather than being attributed to an activity we cannot name.
                foreach (var (quarter, amount) in actualByQuarter.Where(kv => kv.Value is not null))
                {
                    unresolvedByQuarter[quarter] = unresolvedByQuarter.GetValueOrDefault(quarter).GetValueOrDefault() + amount!.Value;
                }

                continue;
            }

            var title = ResolveDisplayTitle(activity, ResolveName(taskNames, activity.TaskId?.Value));
            rows.Add(new FundingSpendUnreportedActivityDto(
                activityId,
                $"{title} (no longer reported)",
                ResolveName(categoryNames, activity.CategoryId?.Value),
                actualByQuarter,
                SumToDate(actualByQuarter, currentQuarter)));
        }

        // Dictionary enumeration order is not a contract, and these rows are frozen into a snapshot that can be
        // regenerated — order them so the same data always renders the same file.
        rows.Sort((a, b) =>
        {
            var byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            return byTitle != 0 ? byTitle : Comparer<Guid?>.Default.Compare(a.ActivityId, b.ActivityId);
        });

        if (unresolvedByQuarter.Count > 0)
        {
            var actualByQuarter = QuarterMap(q => unresolvedByQuarter.GetValueOrDefault(q));
            rows.Add(new FundingSpendUnreportedActivityDto(
                null,
                "Spend recorded against a removed activity",
                null,
                actualByQuarter,
                SumToDate(actualByQuarter, currentQuarter)));
        }

        return rows;
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

        // The collection's version of the module (BLUM-9104): pages and questions as this report was given them.
        var withChildren = await collectionFormModules.GetAsync(collection, SubmissionModules.StrategicFeedback, cancellationToken);
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
        sectionSlug,
        question.ErrorMessage,
        question.MaxLength,
        question.MaxLengthErrorMessage);

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

    private static Dictionary<int, decimal?> QuarterMap(Func<int, decimal?> select) =>
        Enumerable.Range(1, 4).ToDictionary(q => q, select);

    // Actual spend up to and including the reporting quarter. Every "to date" figure in the funding export is summed
    // this way, off the same per-quarter cells the CSV renders, so a row's total always equals its own Q1-Q4 columns.
    private static decimal SumToDate(IReadOnlyDictionary<int, decimal?> actualByQuarter, int currentQuarter) =>
        Enumerable.Range(1, currentQuarter).Sum(q => actualByQuarter.TryGetValue(q, out var v) ? v ?? 0m : 0m);

    // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see ActivitiesControllerService.ResolveOrg),
    // the same reason GetContextAsync above resolves its nullable submission fields this way.
    private static OrganisationId? ResolveOrg(Permission permission) =>
        permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();

    private static string ResolveDisplayTitle(Activity activity, string? taskName) =>
        ActivityDisplayTitle.Resolve(activity, taskName);

    private async Task<Dictionary<Guid, string>> GetTaskNamesAsync(CancellationToken cancellationToken)
    {
        var tasks = await categoryQueries.GetActiveTasksAsync(cancellationToken);
        return tasks.ToDictionary(t => t.Id.Value, t => t.TaskName);
    }

    // Mirrors GetTaskNamesAsync / ActivitiesControllerService.GetCategoryNamesAsync — Activity.CategoryId resolves
    // directly against Category.Name, no join through Task needed.
    private async Task<Dictionary<Guid, string>> GetCategoryNamesAsync(CancellationToken cancellationToken)
    {
        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        return categories.ToDictionary(c => c.Id.Value, c => c.Name);
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