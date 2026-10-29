using System.Text.Json;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Validation;
using Application.Submissions.Exceptions;
using Application.Submissions.Offline;
using Domain.AuditLogs;
using Domain.Submissions;
using LanguageExt;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Submissions.Commands.UploadOfflineReport;

// "Complete your report offline" (BLUM-8310): one section of the quarterly report, filled in a spreadsheet from its
// offline template and uploaded back. The file says which section it is for. A valid file saves that section's
// answers and marks it Completed, exactly as answering the section on the web and pressing Continue on its Check
// page would; an invalid one saves nothing and comes back with its errors written into it. All or nothing: the
// section's writes run in one transaction.
//
// The report is gated before the file is looked at, so a caller who may not write to this report gets the same
// 404/409/422 as every other write, whatever they sent.
public record UploadOfflineReportCommand : IRequest<Either<SubmissionException, OfflineUploadResult>>
{
    public Guid CollectionId { get; init; }

    public IFormFile? File { get; init; }
}

public record OfflineUploadResult(string ModuleCode, Submission Submission);

public class UploadOfflineReportCommandHandler(
    QuarterlyReportWriteGate writeGate,
    OfflineSheetLoader sheetLoader,
    ActivityReportContextResolver activityResolver,
    FundingSpendContextResolver fundingResolver,
    StrategicFeedbackContextResolver feedbackResolver,
    Common.Interfaces.Repositories.ISubmissionSpendEntryRepository spendRepository,
    Common.Interfaces.Repositories.ISubmissionFeedbackAnswerRepository feedbackRepository,
    Common.Interfaces.Repositories.IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
    : IRequestHandler<UploadOfflineReportCommand, Either<SubmissionException, OfflineUploadResult>>
{
    // The OFFLINE_* codes of the 400 envelope and the copy each carries. The copy is the page's error message, so
    // it follows the GDS file upload pattern and never repeats anything from the file.
    public static class Rejections
    {
        public const string FileMissing = "OFFLINE_FILE_MISSING";
        public const string FileEmpty = "OFFLINE_FILE_EMPTY";
        public const string FileTooLarge = "OFFLINE_FILE_TOO_LARGE";
        public const string FileNotCsv = "OFFLINE_FILE_NOT_CSV";
        public const string FileUnreadable = "OFFLINE_FILE_UNREADABLE";
        public const string NotATemplate = "OFFLINE_NOT_A_TEMPLATE";
        public const string WrongReport = "OFFLINE_WRONG_REPORT";
        public const string SectionNotInReport = "OFFLINE_SECTION_NOT_IN_REPORT";
        public const string TemplateOutOfDate = "OFFLINE_TEMPLATE_OUT_OF_DATE";
        public const string ColumnsChanged = "OFFLINE_COLUMNS_CHANGED";
    }

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public async Task<Either<SubmissionException, OfflineUploadResult>> Handle(
        UploadOfflineReportCommand request,
        CancellationToken cancellationToken)
    {
        var gate = await writeGate.ResolveAsync(request.CollectionId, cancellationToken);
        return await gate.MatchAsync<Either<SubmissionException, OfflineUploadResult>>(
            context => UploadAsync(request.File, context, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, OfflineUploadResult>> UploadAsync(
        IFormFile? upload,
        QuarterlyReportWriteContext gate,
        CancellationToken cancellationToken)
    {
        var submissionId = gate.Submission.Id;
        SubmissionException Reject(string code, string message) => new OfflineUploadRejectedException(submissionId, code, message);

        if (upload is null)
        {
            return Reject(Rejections.FileMissing, "Select a CSV file");
        }

        if (upload.Length == 0)
        {
            return Reject(Rejections.FileEmpty, "The selected file is empty");
        }

        if (upload.Length > FieldLimits.MaxOfflineReportUploadBytes)
        {
            return Reject(Rejections.FileTooLarge, "The selected file must be smaller than 5MB");
        }

        byte[] content;
        await using (var stream = upload.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        if (content.AsSpan().StartsWith(ZipSignature))
        {
            return Reject(
                Rejections.FileNotCsv,
                "The selected file must be a CSV. Save your spreadsheet as ‘CSV UTF-8 (Comma delimited)’ and upload it again");
        }

        if (!string.Equals(Path.GetExtension(upload.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(Rejections.FileNotCsv, "The selected file must be a CSV");
        }

        var (file, problem) = OfflineFileReader.Read(content);
        if (file is null)
        {
            return problem == OfflineFileProblem.Unreadable
                ? Reject(Rejections.FileUnreadable, "The selected file could not be read. Save it as ‘CSV UTF-8 (Comma delimited)’ and upload it again")
                : Reject(Rejections.NotATemplate, "The selected file is not a spreadsheet from this report. Download a spreadsheet from this page, fill it in and upload it");
        }

        var identity = file.Identity;
        if (identity.CollectionId != gate.Collection.Id.Value || identity.OrganisationId != gate.OrganisationId.Value)
        {
            return Reject(Rejections.WrongReport, "The selected file is for a different report. Download the spreadsheet for this report and copy your answers into it");
        }

        if (!OfflineSections.IsSupported(identity.ModuleCode))
        {
            return Reject(Rejections.NotATemplate, "The selected file is not a spreadsheet from this report. Download a spreadsheet from this page, fill it in and upload it");
        }

        if (gate.Submission.Sections.All(section => section.ModuleCode != identity.ModuleCode))
        {
            return Reject(Rejections.SectionNotInReport, "The selected file is for a section that is not in this report");
        }

        const string outOfDate = "Your activities have changed since you downloaded this spreadsheet. Download it again and copy your answers into the new one";
        if (identity.Version != OfflineIdentity.CurrentVersion)
        {
            return Reject(Rejections.TemplateOutOfDate, outOfDate);
        }

        var section = await sheetLoader.LoadAsync(gate, identity.ModuleCode, cancellationToken);
        if (!string.Equals(identity.Fingerprint, section.Sheet.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(Rejections.TemplateOutOfDate, outOfDate);
        }

        if (!OfflineUploadValidator.HasExpectedColumns(section.Sheet, file))
        {
            return Reject(
                Rejections.ColumnsChanged,
                "The selected file is missing columns or has changes to row 3. Download the spreadsheet again and copy your answers into it");
        }

        // Each section's own validator. Funding's amounts are read here once and the write uses exactly those.
        OfflineFundingAmounts? fundingAmounts = null;
        var transitive = featureFlags.IsEnabled(Feature.BLUM_9087);
        IReadOnlyList<OfflineCodeError> Rules(IReadOnlyList<OfflineRowAnswers> rows)
        {
            switch (section)
            {
                case ActivityResultsOfflineSection:
                    return OfflineSectionRules.ActivityResults(rows, transitive);
                case FundingSpentOfflineSection funding:
                    fundingAmounts = OfflineSectionRules.FundingAmounts(funding.Context, funding.RemainingQuarters, rows);
                    return OfflineSectionRules.FundingSpent(funding.Sheet, funding.Context, funding.RemainingQuarters, fundingAmounts);
                case StrategicFeedbackOfflineSection feedback:
                    return OfflineSectionRules.StrategicFeedback(feedback.Questions, rows);
                default:
                    throw new InvalidOperationException($"No offline rules for {section.GetType().Name}.");
            }
        }

        var result = OfflineUploadValidator.Validate(section.Sheet, file, Rules, transitive);
        if (!result.IsValid)
        {
            return new OfflineUploadInvalidException(
                submissionId,
                identity.ModuleCode,
                result.Errors,
                OfflineFileAnnotator.Annotate(section.Sheet, file, result),
                OfflineFileAnnotator.FileName(upload.FileName, OfflineSections.SlugFor(identity.ModuleCode)));
        }

        return await transactionRunner.RunAsync<SubmissionException, OfflineUploadResult>(
            async token =>
            {
                try
                {
                    await WriteAsync(section, result, fundingAmounts, token);

                    // BLUM-8747: inside the transaction, so the audit row commits or rolls back with the upload.
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            gate.ActorId,
                            AuditTargetType.Submission,
                            submissionId.Value,
                            AuditAction.SubmissionOfflineReportUploaded,
                            before: null,
                            after: JsonSerializer.Serialize(new { moduleCode = identity.ModuleCode, rowCount = result.Rows.Count }),
                            organisationId: gate.OrganisationId),
                        token);

                    return new OfflineUploadResult(identity.ModuleCode, gate.Submission);
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(submissionId, exception);
                }
            },
            cancellationToken);
    }

    private async Task WriteAsync(
        OfflineSection section,
        OfflineValidationResult result,
        OfflineFundingAmounts? fundingAmounts,
        CancellationToken cancellationToken)
    {
        switch (section)
        {
            case ActivityResultsOfflineSection activities:
                // Every reportable activity is in a valid file (a missing one is an error), so completing each of them
                // and rolling the section up once leaves the section Completed — the same roll-up the web runs after
                // each activity's Check page.
                foreach (var row in result.Rows)
                {
                    var context = ActivityReportContextResolver.For(section.Gate, activities.ActivitiesByReference[row.Row.Reference]);
                    var questions = row.Row.Bindings.Select(binding => binding.Question).ToList();
                    await activityResolver.WriteCompletedReportAsync(context, questions, row.Answers, cancellationToken);
                }

                await activityResolver.RecomputeSectionStatusAsync(
                    section.Gate.Submission, section.Gate.OrganisationId, section.Gate.ActorId, cancellationToken);
                break;

            case FundingSpentOfflineSection funding:
                var amounts = fundingAmounts ?? OfflineSectionRules.FundingAmounts(funding.Context, funding.RemainingQuarters, result.Rows);
                var submissionId = funding.Context.Submission.Id;
                await spendRepository.ReplaceForSubmissionAsync(
                    submissionId,
                    SpendEntryKind.Actual,
                    FundingSpendEntries.Actuals(funding.Context, amounts.Actuals, amounts.OtherActual, dateTimeProvider.GetUtcNow()),
                    cancellationToken);

                // No quarters left to forecast in Q4, and the web leaves the Forecast rows alone then too.
                if (funding.RemainingQuarters.Count > 0)
                {
                    await spendRepository.ReplaceForSubmissionAsync(
                        submissionId,
                        SpendEntryKind.Forecast,
                        FundingSpendEntries.Forecast(funding.Context, funding.RemainingQuarters, amounts.Forecasts, amounts.OtherForecasts, dateTimeProvider.GetUtcNow()),
                        cancellationToken);
                }

                await fundingResolver.SetSectionStatusAsync(funding.Context, SubmissionSectionStatus.Completed, cancellationToken);
                break;

            case StrategicFeedbackOfflineSection feedback:
                // Every question's answer is replaced, so a cell left blank clears what was saved for it.
                var answers = result.Rows.Count == 0 ? new Dictionary<string, string?>() : result.Rows[0].Answers;
                var persistable = StrategicFeedbackAnswerValidator.CollectPersistable(feedback.Questions, answers)
                    .Select(pair => SubmissionFeedbackAnswer.Record(
                        SubmissionFeedbackAnswerId.New(), feedback.Context.Submission.Id, pair.Question.Code, pair.Value, feedback.Context.ActorId, dateTimeProvider.GetUtcNow()))
                    .ToList();
                await feedbackRepository.ReplaceForQuestionsAsync(
                    feedback.Context.Submission.Id,
                    feedback.Questions.Select(question => question.Code).ToList(),
                    persistable,
                    cancellationToken);
                await feedbackResolver.SetSectionStatusAsync(feedback.Context, SubmissionSectionStatus.Completed, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"No offline writer for {section.GetType().Name}.");
        }
    }
}