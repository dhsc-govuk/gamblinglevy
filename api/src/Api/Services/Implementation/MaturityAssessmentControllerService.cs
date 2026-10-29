using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.MaturityAssessments;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Application.Submissions;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Read side of the LA-facing maturity assessment. Reads only require the collection to be an assigned, published
// maturity-assessment collection (a closed assessment stays viewable); the write commands enforce the open window.
public class MaturityAssessmentControllerService(
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IMaturityAssessmentQueries assessmentQueries,
    IMaturityAssessmentAnswerRepository answerRepository,
    IUserQueries userQueries,
    IDateTimeProvider dateTimeProvider) : IMaturityAssessmentControllerService
{
    public async Task<Option<MaturityAssessmentContextDto>> GetContextAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<MaturityAssessmentContextDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var assessment = await assessmentQueries.GetByCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);

        // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (BLUM-7216).
        Guid? assessmentId = assessment.IsNone ? null : assessment.ValueUnsafe().Id.Value;
        DateTime? submittedAt = assessment.IsNone ? null : assessment.ValueUnsafe().SubmittedAt;
        string? referenceNumber = assessment.IsNone ? null : assessment.ValueUnsafe().ReferenceNumber;
        var status = SubmissionStatusRules.Derive(assessment.IsSome, submittedAt, collection.SubmissionDeadline, utcNow);

        // Setup fields are only null while a collection is a draft, and GetEligibleAsync excludes drafts.
        return new MaturityAssessmentContextDto(
            collection.Id.Value,
            collection.Name,
            collection.FinancialYear!,
            MaturityAssessmentReference.EndYear(collection.FinancialYear!),
            collection.ReportingPeriodStart!.Value,
            collection.ReportingPeriodEnd!.Value,
            collection.SubmissionDeadline!.Value,
            collection.GetStatus(utcNow).ToString(),
            SubmissionEligibility.IsOpen(collection, utcNow),
            organisation.Name,
            assessmentId,
            status,
            submittedAt,
            referenceNumber);
    }

    public async Task<Option<MaturityTaskListDto>> GetTaskListAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<MaturityTaskListDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var assessment = await assessmentQueries.GetByCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (assessment.IsNone)
        {
            return Option<MaturityTaskListDto>.None;
        }

        return await ToTaskListDtoAsync(assessment.ValueUnsafe(), collection, cancellationToken);
    }

    public async Task<MaturityTaskListDto> ToTaskListDtoAsync(MaturityAssessment assessment, CancellationToken cancellationToken)
    {
        var collection = await collectionQueries.GetByIdAsync(assessment.CollectionId, cancellationToken: cancellationToken);
        if (collection.IsNone)
        {
            throw new InvalidOperationException($"Collection {assessment.CollectionId.Value} of maturity assessment {assessment.Id.Value} was not found.");
        }

        return await ToTaskListDtoAsync(assessment, collection.ValueUnsafe(), cancellationToken);
    }

    // The LA's sections are the collection's current forms (BLUM-8093) — frozen once submitted — so the list, the
    // submit gate and the pre-filled banner all read them rather than the whole catalogue or the rows Start created.
    private async Task<MaturityTaskListDto> ToTaskListDtoAsync(
        MaturityAssessment assessment,
        Collection collection,
        CancellationToken cancellationToken)
    {
        var sectionCodes = MaturityAssessmentSections.For(assessment, collection);
        var statusByCode = assessment.Sections.ToDictionary(s => s.SectionCode, s => s.Status.ToString(), StringComparer.Ordinal);
        var sections = ToSectionSummaries(sectionCodes, statusByCode);

        var included = sectionCodes.ToHashSet(StringComparer.Ordinal);
        var questionCodes = MaturityQuestionResolver.ResolveAll()
            .Where(q => included.Contains(q.Section))
            .Select(q => q.Code)
            .ToHashSet(StringComparer.Ordinal);

        var answers = await answerRepository.GetForAssessmentAsync(assessment.Id, cancellationToken);
        var prefillSources = answers
            .Where(a => !a.Source.IsUser && questionCodes.Contains(a.QuestionCode))
            .Select(a => a.Source.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return new MaturityTaskListDto(
            assessment.Id.Value,
            sections,
            CanSubmit: !assessment.IsSubmitted && assessment.AreSectionsCompleted(sectionCodes),
            assessment.IsSubmitted,
            assessment.ReferenceNumber,
            assessment.SubmittedAt,
            IsPrefilled: prefillSources.Count > 0,
            prefillSources);
    }

    public async Task<Option<MaturitySectionDto>> GetSectionAsync(
        Permission permission,
        Guid collectionId,
        string sectionCode,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<MaturitySectionDto>.None;
        }

        var (collection, organisation) = eligible.Value;

        // A section the admin left off the collection is not the LA's to read (BLUM-8093) — 404, like an unknown code.
        var section = MaturityProtocol.FindSection(sectionCode);
        if (section is null || !MaturityCollectionForms.Includes(collection, section.Code))
        {
            return Option<MaturitySectionDto>.None;
        }

        var assessmentLookup = await assessmentQueries.GetByCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (assessmentLookup.IsNone)
        {
            return Option<MaturitySectionDto>.None;
        }

        var assessment = assessmentLookup.ValueUnsafe();
        var questions = MaturityQuestionResolver.ResolveSection(section.Code);
        var pages = section.Pages
            .Select(page => new MaturityPageDto(
                page.Code,
                page.Title,
                page.VisibleWhen is null ? null : new MaturityPageVisibilityDto(page.VisibleWhen.QuestionCode, page.VisibleWhen.Values),
                questions.Where(q => q.Page == page.Code).Select(ToMaturityQuestionDto).ToList()))
            .ToList();

        var codes = questions.Select(q => q.Code).ToHashSet(StringComparer.Ordinal);
        var saved = await answerRepository.GetForAssessmentAsync(assessment.Id, cancellationToken);
        var answers = saved
            .Where(a => codes.Contains(a.QuestionCode))
            .Select(a => new MaturityAnswerDto(a.QuestionCode, a.Value, a.Source.ToString()))
            .ToList();

        var status = assessment.Sections.FirstOrDefault(s => s.SectionCode == section.Code)?.Status.ToString()
            ?? MaturityAssessmentSectionStatuses.NotStarted;

        return new MaturitySectionDto(assessment.Id.Value, section.Code, section.Title, status, pages, answers);
    }

    public async Task<Option<SubmittedMaturityAssessmentDto>> GetSubmittedAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var eligible = await GetEligibleAsync(permission, collectionId, cancellationToken);
        if (eligible is null)
        {
            return Option<SubmittedMaturityAssessmentDto>.None;
        }

        var (collection, organisation) = eligible.Value;
        var detailLookup = await assessmentQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (detailLookup.IsNone)
        {
            return Option<SubmittedMaturityAssessmentDto>.None;
        }

        var detail = detailLookup.ValueUnsafe();
        if (detail.SubmittedAt is not { } submittedAt)
        {
            return Option<SubmittedMaturityAssessmentDto>.None;
        }

        var (submittedByName, submittedByEmail) = await ResolveSubmitterAsync(detail.SubmittedBy, cancellationToken);
        var snapshot = MaturityAssessmentSnapshotJson.TryRead(detail.SnapshotJson) ?? new MaturityAssessmentSnapshotDocument([]);

        return new SubmittedMaturityAssessmentDto(
            collection.Id.Value,
            collection.Name,
            collection.FinancialYear!,
            MaturityAssessmentReference.EndYear(collection.FinancialYear!),
            organisation.Name,
            detail.ReferenceNumber ?? string.Empty,
            submittedAt,
            submittedByName,
            submittedByEmail,
            ToSnapshotDtos(snapshot));
    }

    public ReportQuestionDto ToReportQuestionDto(MaturityResolvedQuestion question) => new(
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
        null,
        null,
        null,
        question.Page,
        question.ErrorMessage,
        question.MaxLength,
        question.MaxLengthErrorMessage);

    // The given sections (the collection's forms — MaturityCollectionForms.SectionCodesFor) in catalogue order, with
    // catalogue titles: the section rows only persist codes and statuses. A section the row set does not know (a form
    // the admin added after Start) reads as not started rather than vanishing.
    public static IReadOnlyList<MaturitySectionSummaryDto> ToSectionSummaries(
        IReadOnlyCollection<string> sectionCodes,
        IReadOnlyDictionary<string, string> statusByCode) =>
        MaturityProtocol.Sections
            .Where(s => sectionCodes.Contains(s.Code, StringComparer.Ordinal))
            .Select(s => new MaturitySectionSummaryDto(
                s.Code,
                s.Title,
                statusByCode.TryGetValue(s.Code, out var status) ? status : MaturityAssessmentSectionStatuses.NotStarted))
            .ToList();

    public static IReadOnlyList<MaturitySnapshotSectionDto> ToSnapshotDtos(MaturityAssessmentSnapshotDocument snapshot) =>
        snapshot.Sections
            .Select(s => new MaturitySnapshotSectionDto(
                s.Code,
                s.Title,
                s.Pages.Select(p => new MaturitySnapshotPageDto(
                    p.Code,
                    p.Title,
                    p.Rows.Select(r => new MaturitySnapshotRowDto(r.Code, r.Question, r.Answer, r.Value, r.GroupLabel)).ToList())).ToList()))
            .ToList();

    private static MaturityQuestionDto ToMaturityQuestionDto(MaturityResolvedQuestion question) => new(
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
        question.SecondaryOptions.Select(o => new ReportQuestionOptionDto(o.Value, o.Label)).ToList(),
        question.Section,
        question.Page,
        question.GroupLabel,
        question.ExclusiveOptionValue,
        question.ErrorMessage,
        question.MaxLength,
        question.MaxLengthErrorMessage);

    private async Task<(string? Name, string? Email)> ResolveSubmitterAsync(Guid? submittedBy, CancellationToken cancellationToken)
    {
        if (submittedBy is not { } userId)
        {
            return (null, null);
        }

        var userLookup = await userQueries.FindById(new UserId(userId), cancellationToken);
        if (userLookup.IsNone)
        {
            return (null, null);
        }

        var user = userLookup.ValueUnsafe();
        return (user.Name.ToString(), user.Email);
    }

    // The authoritative, type-aware gate: a PUBLISHED maturity-assessment collection assigned to the caller's
    // organisation. Drafts are excluded here (not just in the context read) because every DTO below null-forgives
    // the setup fields.
    private async Task<(Collection Collection, Organisation Organisation)?> GetEligibleAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var organisationId = permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
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
        if (collection.IsDraft
            || !SubmissionEligibility.IsMaturityAssessment(collection)
            || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return null;
        }

        return (collection, organisation);
    }
}