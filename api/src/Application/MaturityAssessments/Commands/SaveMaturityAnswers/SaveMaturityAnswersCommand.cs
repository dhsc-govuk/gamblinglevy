using System.Text.Json;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Exceptions;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.MaturityAssessments;
using LanguageExt;
using MediatR;

namespace Application.MaturityAssessments.Commands.SaveMaturityAnswers;

// Saves one PAGE of a maturity-assessment section. The client sends that page's question codes (cleared ones as
// empty); only those codes are replaced, so saving one page never wipes another's answers — and an untouched
// pre-filled page keeps its non-user Source. SaveAsDraft ("Save and come back later") skips required enforcement;
// Continue (SaveAsDraft = false) enforces it. Free text has no protocol limit (BLUM-8411), and the FieldLimits.AnswerText
// ceiling applies to both. Never completes the section — that is CompleteMaturitySectionCommand.
public record MaturityAnswerInput(string QuestionCode, string? Value);

public record SaveMaturityAnswersCommand : IRequest<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>
{
    public Guid CollectionId { get; init; }
    public string SectionCode { get; init; } = string.Empty;
    public string PageCode { get; init; } = string.Empty;
    public bool SaveAsDraft { get; init; }
    public IReadOnlyList<MaturityAnswerInput> Answers { get; init; } = [];
}

public class SaveMaturityAnswersCommandHandler(
    MaturityAssessmentContextResolver resolver,
    IMaturityAssessmentAnswerRepository answerRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
    : IRequestHandler<SaveMaturityAnswersCommand, Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>
{
    public async Task<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>> Handle(
        SaveMaturityAnswersCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveForEditAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>(
            ctx => SaveAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>> SaveAsync(
        SaveMaturityAnswersCommand request,
        MaturityAssessmentContext context,
        CancellationToken cancellationToken)
    {
        // An unknown code and a section the admin left off the collection (BLUM-8093) are both 404: the LA cannot
        // answer, or create a row for, a section that is not on their task list.
        if (MaturityProtocol.FindSection(request.SectionCode) is null
            || !MaturityCollectionForms.Includes(context.Collection, request.SectionCode))
        {
            return new MaturityAssessmentSectionUnknownException(context.Assessment.Id, request.SectionCode);
        }

        var pageQuestions = MaturityQuestionResolver.ResolvePage(request.SectionCode, request.PageCode);
        if (pageQuestions.Count == 0)
        {
            return new MaturityAssessmentPageUnknownException(context.Assessment.Id, request.SectionCode, request.PageCode);
        }

        // Visibility of a page's question can hang off an answer on an EARLIER page (a whole Needs-assessment page
        // is gated on "Has your local authority published a needs assessment?"), so the request is overlaid on the
        // saved answers before anything is validated.
        var submitted = BuildAnswersMap(request.Answers);
        var saved = await resolver.GetSavedAnswersAsync(context.Assessment.Id, cancellationToken);
        var merged = new Dictionary<string, string?>(saved, StringComparer.Ordinal);
        foreach (var (code, value) in submitted)
        {
            merged[code] = value;
        }

        // The page's questions are judged among the whole section's, so a page rule's parent on an earlier page counts as
        // hidden when it is. BLUM_9087 is what makes that transitive; off, each rule reads its parent's answer alone.
        var visibility = MaturityAnswerValidator.Visibility(
            MaturityQuestionResolver.ResolveSection(request.SectionCode), merged, featureFlags.IsEnabled(Feature.BLUM_9087));
        var errors = MaturityAnswerValidator.Validate(pageQuestions, merged, enforceRequired: !request.SaveAsDraft, visibility);
        if (errors.Count > 0)
        {
            return new MaturityAnswersInvalidException(context.Assessment.Id, errors);
        }

        // The answers, the section status and the audit row commit together (BLUM-8748); the answer replace joins
        // this transaction rather than opening its own.
        return await transactionRunner.RunAsync<MaturityAssessmentException, MaturityAssessmentSectionStatus>(
            async token =>
            {
                try
                {
                    var submittedCodes = submitted.Keys.ToList();
                    var answers = MaturityAnswerValidator.CollectPersistable(pageQuestions, merged, visibility)
                        .Where(p => submitted.ContainsKey(p.Question.Code))
                        .Select(p => MaturityAssessmentAnswer.Record(
                            MaturityAssessmentAnswerId.New(),
                            context.Assessment.Id,
                            p.Question.Code,
                            p.Value,
                            AnswerSource.User,
                            context.ActorId,
                            dateTimeProvider.GetUtcNow()))
                        .ToList();

                    await answerRepository.ReplaceForQuestionsAsync(context.Assessment.Id, submittedCodes, answers, token);
                    var status = await resolver.RecomputeDraftStatusAsync(context, request.SectionCode, token);
                    await AppendAuditAsync(request, context, submittedCodes, token);

                    return status;
                }
                catch (Exception exception)
                {
                    return new MaturityAssessmentUnknownException(context.Assessment.Id, exception);
                }
            },
            cancellationToken);
    }

    // Question codes only — never the answer values, which are free text the LA typed.
    private async Task AppendAuditAsync(
        SaveMaturityAnswersCommand request,
        MaturityAssessmentContext context,
        IReadOnlyList<string> questionCodes,
        CancellationToken cancellationToken)
    {
        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                context.ActorId,
                AuditTargetType.MaturityAssessment,
                context.Assessment.Id.Value,
                AuditAction.MaturityAssessmentAnswersSaved,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    sectionCode = request.SectionCode,
                    pageCode = request.PageCode,
                    questionCodes,
                }),
                context.Organisation.Id),
            cancellationToken);
    }

    // Last value wins for a repeated code; blank codes are ignored. Ordinal to match the stable question codes.
    private static Dictionary<string, string?> BuildAnswersMap(IReadOnlyList<MaturityAnswerInput> inputs)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.QuestionCode))
            {
                map[input.QuestionCode.Trim()] = input.Value;
            }
        }

        return map;
    }
}