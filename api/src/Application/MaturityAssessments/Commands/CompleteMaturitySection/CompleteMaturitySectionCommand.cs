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

namespace Application.MaturityAssessments.Commands.CompleteMaturitySection;

// The section's "Check your answers" Continue: validates that every required, visible question of the section has
// a saved answer, then marks the section Completed. The client posts no body — the saved answers are validated.
public record CompleteMaturitySectionCommand : IRequest<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>
{
    public Guid CollectionId { get; init; }
    public string SectionCode { get; init; } = string.Empty;
}

public class CompleteMaturitySectionCommandHandler(
    MaturityAssessmentContextResolver resolver,
    IMaturityAssessmentRepository assessmentRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
    : IRequestHandler<CompleteMaturitySectionCommand, Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>
{
    public async Task<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>> Handle(
        CompleteMaturitySectionCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveForEditAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>>(
            ctx => CompleteAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessmentSectionStatus>> CompleteAsync(
        CompleteMaturitySectionCommand request,
        MaturityAssessmentContext context,
        CancellationToken cancellationToken)
    {
        var questions = MaturityQuestionResolver.ResolveSection(request.SectionCode);
        if (questions.Count == 0 || !MaturityCollectionForms.Includes(context.Collection, request.SectionCode))
        {
            return new MaturityAssessmentSectionUnknownException(context.Assessment.Id, request.SectionCode);
        }

        var saved = await resolver.GetSavedAnswersAsync(context.Assessment.Id, cancellationToken);
        var errors = MaturityAnswerValidator.Validate(questions, saved, enforceRequired: true, transitive: featureFlags.IsEnabled(Feature.BLUM_9087));
        if (errors.Count > 0)
        {
            return new MaturityAnswersInvalidException(context.Assessment.Id, errors);
        }

        // The section status and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<MaturityAssessmentException, MaturityAssessmentSectionStatus>(
            async token =>
            {
                try
                {
                    context.Assessment.SetSectionStatus(request.SectionCode, MaturityAssessmentSectionStatus.Completed, context.ActorId, dateTimeProvider.GetUtcNow());
                    await assessmentRepository.UpdateAsync(context.Assessment, token);
                    await AppendAuditAsync(request.SectionCode, context, token);

                    return MaturityAssessmentSectionStatus.Completed;
                }
                catch (Exception exception)
                {
                    return new MaturityAssessmentUnknownException(context.Assessment.Id, exception);
                }
            },
            cancellationToken);
    }

    private async Task AppendAuditAsync(string sectionCode, MaturityAssessmentContext context, CancellationToken cancellationToken)
    {
        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                context.ActorId,
                AuditTargetType.MaturityAssessment,
                context.Assessment.Id.Value,
                AuditAction.MaturityAssessmentSectionCompleted,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    sectionCode,
                    status = MaturityAssessmentSectionStatus.Completed.ToString(),
                }),
                context.Organisation.Id),
            cancellationToken);
    }
}