using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Exceptions;
using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.MaturityAssessments;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.MaturityAssessments.Commands.StartMaturityAssessment;

// "Start" the annual maturity assessment for the caller's organisation against one open maturity-assessment
// collection: one Not-started section per form the collection includes, then the pre-filled answers (BLUM-7856) written as rows
// carrying their source. Sections deliberately stay Not started after pre-fill — the LA has not reviewed anything
// yet, and the task list's "pre-filled" banner is what tells them to.
public record StartMaturityAssessmentCommand : IRequest<Either<MaturityAssessmentException, MaturityAssessment>>
{
    public Guid CollectionId { get; init; }
}

public class StartMaturityAssessmentCommandHandler(
    MaturityAssessmentContextResolver resolver,
    IMaturityAssessmentQueries assessmentQueries,
    IMaturityAssessmentRepository assessmentRepository,
    IMaturityAssessmentAnswerRepository answerRepository,
    IMaturityPrefillService prefillService,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider,
    ILogger<StartMaturityAssessmentCommandHandler> logger)
    : IRequestHandler<StartMaturityAssessmentCommand, Either<MaturityAssessmentException, MaturityAssessment>>
{
    public async Task<Either<MaturityAssessmentException, MaturityAssessment>> Handle(
        StartMaturityAssessmentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveForStartAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<MaturityAssessmentException, MaturityAssessment>>(
            ctx => StartAsync(ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessment>> StartAsync(
        MaturityAssessmentStartContext context,
        CancellationToken cancellationToken)
    {
        var existing = await assessmentQueries.GetByCollectionAndOrgAsync(context.Collection.Id, context.Organisation.Id, cancellationToken);
        if (existing.IsSome)
        {
            return new MaturityAssessmentAlreadyExistsException(existing.Match(a => a.Id, MaturityAssessmentId.Empty));
        }

        // Only the forms the admin kept on the collection (BLUM-8093). Publishing requires at least one, so an empty
        // set is a collection the LA cannot answer — not eligible, rather than a Guard failure surfacing as a 500.
        var sectionCodes = MaturityCollectionForms.SectionCodesFor(context.Collection);
        if (sectionCodes.Count == 0)
        {
            return new MaturityAssessmentCollectionNotEligibleException(context.Collection.Id);
        }

        var assessment = MaturityAssessment.Start(
            MaturityAssessmentId.New(),
            context.Collection.Id,
            context.Organisation.Id,
            sectionCodes,
            context.ActorId,
            dateTimeProvider.GetUtcNow());

        var prefilledBySource = new Dictionary<string, int>(StringComparer.Ordinal);
        var prefillFailed = false;
        IReadOnlyList<MaturityAssessmentAnswer> rows = [];

        // Best-effort, and built BEFORE the transaction: a pre-fill failure must never stop the LA starting — they
        // simply open a blank assessment, which is today's behaviour for every question without a source. The
        // sources swallow their own read failures, which cannot happen inside a transaction (a failed statement
        // aborts it), and none of them reads the assessment being started.
        try
        {
            // Pre-fill only what the LA will be asked: an excluded section's answers would never be shown, yet would
            // still mark the task list as pre-filled.
            var included = sectionCodes.ToHashSet(StringComparer.Ordinal);
            var questions = MaturityQuestionResolver.ResolveAll().Where(q => included.Contains(q.Section)).ToList();
            var known = questions.ToDictionary(q => q.Code, StringComparer.Ordinal);
            var prefilled = await prefillService.BuildAsync(context.Organisation, context.Collection, questions, cancellationToken);

            rows = prefilled
                .Where(p => known.ContainsKey(p.QuestionCode) && !string.IsNullOrWhiteSpace(p.Value))
                .GroupBy(p => p.QuestionCode, StringComparer.Ordinal)
                .Select(g => g.First())
                .Select(p => MaturityAssessmentAnswer.Record(
                    MaturityAssessmentAnswerId.New(),
                    assessment.Id,
                    p.QuestionCode,
                    p.Value,
                    p.Source,
                    context.ActorId,
                    dateTimeProvider.GetUtcNow()))
                .ToList();

            foreach (var group in rows.GroupBy(row => row.Source.ToString(), StringComparer.Ordinal))
            {
                prefilledBySource[group.Key] = group.Count();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to pre-fill maturity assessment {AssessmentId} for organisation {OrganisationId}; the assessment was started blank.",
                assessment.Id,
                context.Organisation.Id);
            prefillFailed = true;
            rows = [];
            prefilledBySource.Clear();
        }

        // The assessment, its pre-filled answers and the audit row commit together (BLUM-8748), so the audit's
        // counts are exactly the rows that landed.
        var result = await TryStartAsync(context, assessment, sectionCodes, rows, prefilledBySource, prefillFailed, cancellationToken);

        // The pre-fill must still never stop the LA starting. The task list starts the assessment automatically and
        // every retry builds the same rows, so one value the insert refuses would fail the start on every visit and
        // the LA could never open it. A failure with rows in it therefore gets one more attempt without them: the
        // assessment starts blank and its audit row says the pre-fill failed, as when building the rows fails above.
        // A duplicate is not retried — that is another start winning, not the pre-fill.
        if (rows.Count > 0 && result.Match(_ => false, left => left is MaturityAssessmentUnknownException))
        {
            result.IfLeft(e => logger.LogError(
                e,
                "Failed to insert the pre-filled answers for maturity assessment {AssessmentId} for organisation {OrganisationId}; starting it blank.",
                assessment.Id,
                context.Organisation.Id));

            result = await TryStartAsync(
                context,
                assessment,
                sectionCodes,
                [],
                new Dictionary<string, int>(StringComparer.Ordinal),
                prefillFailed: true,
                cancellationToken);
        }

        if (result.Match(_ => false, left => left is MaturityAssessmentAlreadyExistsException))
        {
            var winner = await assessmentQueries.GetByCollectionAndOrgAsync(context.Collection.Id, context.Organisation.Id, cancellationToken);
            return new MaturityAssessmentAlreadyExistsException(winner.Match(a => a.Id, MaturityAssessmentId.Empty));
        }

        return result;
    }

    // One attempt, in one transaction. A Left rolls it back and clears the change tracker, so a second attempt adds
    // the assessment afresh.
    private Task<Either<MaturityAssessmentException, MaturityAssessment>> TryStartAsync(
        MaturityAssessmentStartContext context,
        MaturityAssessment assessment,
        IReadOnlyCollection<string> sectionCodes,
        IReadOnlyList<MaturityAssessmentAnswer> rows,
        IReadOnlyDictionary<string, int> prefilledBySource,
        bool prefillFailed,
        CancellationToken cancellationToken) =>
        transactionRunner.RunAsync<MaturityAssessmentException, MaturityAssessment>(
            async token =>
            {
                try
                {
                    // The IsNone read above and this insert are not atomic: the task list auto-starts, so two tabs or
                    // two colleagues can both pass the read. The unique index decides, and the loser is told 409 like
                    // a sequential second start would be — the winner is looked up after this transaction rolls
                    // back, because the violation has aborted it.
                    if (!await assessmentRepository.TryAddAsync(assessment, token))
                    {
                        return new MaturityAssessmentAlreadyExistsException(MaturityAssessmentId.Empty());
                    }

                    await answerRepository.InsertAsync(rows, token);
                    await AppendAuditAsync(context, assessment, sectionCodes, prefilledBySource, prefillFailed, token);

                    return assessment;
                }
                catch (Exception exception)
                {
                    return new MaturityAssessmentUnknownException(assessment.Id, exception);
                }
            },
            cancellationToken);

    // Counts only, never values.
    private async Task AppendAuditAsync(
        MaturityAssessmentStartContext context,
        MaturityAssessment assessment,
        IReadOnlyCollection<string> sectionCodes,
        IReadOnlyDictionary<string, int> prefilledBySource,
        bool prefillFailed,
        CancellationToken cancellationToken)
    {
        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                context.ActorId,
                AuditTargetType.MaturityAssessment,
                assessment.Id.Value,
                AuditAction.MaturityAssessmentStarted,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    collectionId = context.Collection.Id.Value,
                    financialYear = context.Collection.FinancialYear,
                    sections = sectionCodes,
                    prefilledAnswers = prefilledBySource,
                    prefillFailed,
                }),
                context.Organisation.Id),
            cancellationToken);
    }
}