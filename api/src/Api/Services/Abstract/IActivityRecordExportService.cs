using Application.Common.Permissions;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IActivityRecordExportService
{
    // Builds the "Download activity record" CSV (BLUM-7648): every field of the activity record, then what each of the
    // organisation's submitted quarterly reports said about it. None when the activity does not exist, belongs to
    // another organisation or is soft-deleted — the same 404 the activity page itself gets.
    Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid activityId, CancellationToken cancellationToken);
}