using Api.Dtos;
using Application.Common.Permissions;
using Application.MaturityAssessments.Protocol;
using Domain.MaturityAssessments;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IMaturityAssessmentControllerService
{
    // One assigned maturity-assessment collection plus the caller organisation's assessment state against it. None
    // when the collection does not exist, is a draft, is not a maturity assessment, or is not assigned to the
    // caller's organisation. Deliberately does not require an open window — a closed assessment stays viewable.
    Task<Option<MaturityAssessmentContextDto>> GetContextAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    Task<Option<MaturityTaskListDto>> GetTaskListAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // Folds a command-returned assessment into the task-list DTO.
    Task<MaturityTaskListDto> ToTaskListDtoAsync(MaturityAssessment assessment, CancellationToken cancellationToken);

    // One section's pages, questions and saved answers (with their sources).
    Task<Option<MaturitySectionDto>> GetSectionAsync(Permission permission, Guid collectionId, string sectionCode, CancellationToken cancellationToken);

    // The frozen, read-only assessment once submitted; None until then.
    Task<Option<SubmittedMaturityAssessmentDto>> GetSubmittedAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // The wire shape of one resolved question — shared with the snapshot service so ReportAnswerPresenter renders
    // maturity answers exactly as it renders quarterly ones.
    ReportQuestionDto ToReportQuestionDto(MaturityResolvedQuestion question);
}