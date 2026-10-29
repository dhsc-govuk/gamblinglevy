using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.MaturityAssessments.Exceptions;
using Application.MaturityAssessments.Protocol;
using Application.Submissions;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.MaturityAssessments;

// The resolved subject of a maturity-assessment write: the eligible collection, the acting organisation/user and —
// for everything but Start — the tracked assessment. Mirrors StrategicFeedbackContextResolver so the Start, Save,
// Complete and Submit commands share one gate.
public record MaturityAssessmentStartContext(Collection Collection, Organisation Organisation, UserId ActorId);

public record MaturityAssessmentContext(
    MaturityAssessment Assessment,
    Collection Collection,
    Organisation Organisation,
    UserId ActorId);

public class MaturityAssessmentContextResolver(
    IMaturityAssessmentQueries assessmentQueries,
    IMaturityAssessmentRepository assessmentRepository,
    IMaturityAssessmentAnswerRepository answerRepository,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
{
    // Eligibility for STARTING: a published maturity-assessment collection assigned to the caller's organisation,
    // currently Open. 404 for anything not eligible (assignment is not leaked); 422 REPORT_NOT_OPEN when closed
    // or scheduled.
    public async Task<Either<MaturityAssessmentException, MaturityAssessmentStartContext>> ResolveForStartAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<Either<MaturityAssessmentException, MaturityAssessmentStartContext>>(
                permission => ResolveStartWithPermission(collectionId, permission, cancellationToken),
                error => new MaturityAssessmentForbiddenException(MaturityAssessmentId.Empty(), error.Message));
    }

    // Eligibility for EDITING: everything Start requires, plus a started, not-yet-submitted assessment loaded WITH
    // tracking so section status changes are diffed on save.
    public async Task<Either<MaturityAssessmentException, MaturityAssessmentContext>> ResolveForEditAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var start = await ResolveForStartAsync(collectionId, cancellationToken);
        return await start.MatchAsync<Either<MaturityAssessmentException, MaturityAssessmentContext>>(
            async ctx =>
            {
                var lookup = await assessmentQueries.GetByCollectionAndOrgAsync(ctx.Collection.Id, ctx.Organisation.Id, cancellationToken);
                if (lookup.IsNone)
                {
                    return new MaturityAssessmentDoesNotExistException(ctx.Collection.Id);
                }

                var assessment = lookup.ValueUnsafe();
                if (assessment.IsSubmitted)
                {
                    return new MaturityAssessmentAlreadySubmittedException(assessment.Id);
                }

                return new MaturityAssessmentContext(assessment, ctx.Collection, ctx.Organisation, ctx.ActorId);
            },
            error => error);
    }

    // After a draft save: In progress when any saved answer belongs to one of the section's questions, otherwise
    // Not started. Never Completed — only the section's "Check your answers" Continue completes it, so a later
    // draft save drops a Completed section back to In progress.
    public async Task<MaturityAssessmentSectionStatus> RecomputeDraftStatusAsync(
        MaturityAssessmentContext context,
        string sectionCode,
        CancellationToken cancellationToken)
    {
        var codes = MaturityQuestionResolver.ResolveSection(sectionCode).Select(q => q.Code).ToHashSet(StringComparer.Ordinal);
        var answers = await answerRepository.GetForAssessmentAsync(context.Assessment.Id, cancellationToken);
        var status = answers.Any(a => codes.Contains(a.QuestionCode))
            ? MaturityAssessmentSectionStatus.InProgress
            : MaturityAssessmentSectionStatus.NotStarted;

        context.Assessment.SetSectionStatus(sectionCode, status, context.ActorId, dateTimeProvider.GetUtcNow());
        await assessmentRepository.UpdateAsync(context.Assessment, cancellationToken);
        return status;
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetSavedAnswersAsync(
        MaturityAssessmentId assessmentId,
        CancellationToken cancellationToken)
    {
        var saved = await answerRepository.GetForAssessmentAsync(assessmentId, cancellationToken);
        return saved.ToDictionary(a => a.QuestionCode, a => (string?)a.Value, StringComparer.Ordinal);
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessmentStartContext>> ResolveStartWithPermission(
        Guid collectionId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new MaturityAssessmentForbiddenException(MaturityAssessmentId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var collectionRef = new CollectionId(collectionId);

        var collectionLookup = await collectionQueries.GetByIdAsync(collectionRef, cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return new MaturityAssessmentCollectionNotEligibleException(collectionRef);
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return new MaturityAssessmentCollectionNotEligibleException(collectionRef);
        }

        var organisation = organisationLookup.ValueUnsafe();
        var collection = collectionLookup.ValueUnsafe();

        // IsDraft is checked explicitly even though a draft has no Type: an admin can revert a PUBLISHED window to
        // draft and clear its year, and every setup field is nullable from that moment (BLUM-7216 review).
        if (collection.IsDraft
            || !SubmissionEligibility.IsMaturityAssessment(collection)
            || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return new MaturityAssessmentCollectionNotEligibleException(collectionRef);
        }

        if (!SubmissionEligibility.IsOpen(collection, utcNow))
        {
            return new MaturityAssessmentNotOpenException(collectionRef, collection.GetStatus(utcNow));
        }

        return new MaturityAssessmentStartContext(collection, organisation, actorId);
    }
}