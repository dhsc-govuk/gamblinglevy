using System.Text.Json;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;

namespace Application.Collections.Audit;

// BLUM-9103 / BLUM-9106: the audit row written when a collection whose window has not opened moves onto a newer version
// of its forms. Before and After list only the forms that moved: formVersions = [{ moduleId, code, versionNumber }],
// with After also carrying the source of the new version. PublishFormModuleCommand writes it through
// IAuditLogRepository; FormModuleVersionSync runs in the boot scope, so it adds the row and stamps it itself.
public static class CollectionFormVersionsFollowedAudit
{
    // DHSC published the form in the forms builder: a new version, or the latest unchanged one a collection was behind.
    public const string PublishSource = "publish";

    // A release changed a published form, and FormModuleVersionSync saved the change as a new version on this boot.
    public const string ReleaseSource = "release";

    // FormModuleVersionSync found the collection behind a version saved earlier (one published before BLUM-9103's
    // rule, for instance) and caught it up on boot.
    public const string CatchUpSource = "catch_up";

    public static AuditLog Create(
        UserId actor,
        CollectionId collectionId,
        IReadOnlyList<FormVersionMove> moves,
        IReadOnlyDictionary<FormModuleVersionId, int> versionNumbers,
        IReadOnlyDictionary<FormModuleId, string> moduleCodes,
        string source)
    {
        object Entry(FormVersionMove move, FormModuleVersionId versionId) => new
        {
            moduleId = move.FormModuleId.Value,
            code = moduleCodes.GetValueOrDefault(move.FormModuleId),
            versionNumber = versionNumbers.TryGetValue(versionId, out var number) ? number : (int?)null,
        };

        return AuditLog.Create(
            actor,
            AuditTargetType.Collection,
            collectionId.Value,
            AuditAction.CollectionFormVersionsFollowed,
            before: JsonSerializer.Serialize(new { formVersions = moves.Select(m => Entry(m, m.From)) }),
            after: JsonSerializer.Serialize(new { formVersions = moves.Select(m => Entry(m, m.To)), source }));
    }
}