using System.Globalization;
using Api.Dtos;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Submissions;
using Application.Submissions.Exceptions;
using Application.Submissions.Offline;
using LanguageExt;

namespace Api.Services.Implementation;

// A section's offline template. It sits behind the same write gate as the upload — the window open, the report not
// submitted, the activities confirmed — because a template for a report that cannot take the upload would only
// mislead: the person would fill it in and then be refused.
public class QuarterlyReportOfflineControllerService(
    QuarterlyReportWriteGate writeGate,
    OfflineSheetLoader sheetLoader) : IQuarterlyReportOfflineControllerService
{
    public async Task<Either<SubmissionException, OfflineTemplateFileDto>> GetTemplateAsync(
        Guid collectionId,
        string moduleCode,
        CancellationToken cancellationToken)
    {
        var gate = await writeGate.ResolveAsync(collectionId, cancellationToken);
        return await gate.MatchAsync<Either<SubmissionException, OfflineTemplateFileDto>>(
            async context =>
            {
                if (context.Submission.Sections.All(section => section.ModuleCode != moduleCode))
                {
                    return new OfflineSectionNotInReportException(context.Submission.Id, moduleCode);
                }

                var section = await sheetLoader.LoadAsync(context, moduleCode, cancellationToken);
                return new OfflineTemplateFileDto(OfflineTemplateWriter.Write(section.Sheet), FileName(context, moduleCode));
            },
            error => error);
    }

    // "activity-results-Barnsley-Q1-2026-27.csv": the section first, so three downloads sort together.
    private static string FileName(QuarterlyReportWriteContext context, string moduleCode)
    {
        var parts = new List<string> { OfflineSections.SlugFor(moduleCode) };

        var organisation = ExportFileName.NameSegment(context.Organisation.Name);
        if (organisation.Length > 0)
        {
            parts.Add(organisation);
        }

        if (context.Collection.Quarter is { } quarter)
        {
            parts.Add($"Q{quarter.ToString(CultureInfo.InvariantCulture)}");
        }

        var financialYear = ExportFileName.Slug(context.Collection.FinancialYear ?? string.Empty);
        if (financialYear.Length > 0)
        {
            parts.Add(financialYear);
        }

        return $"{string.Join('-', parts)}.csv";
    }
}