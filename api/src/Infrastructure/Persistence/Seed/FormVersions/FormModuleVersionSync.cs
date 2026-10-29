using System.Text.Json;
using Application.Collections.Audit;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seed.FormVersions;

// BLUM-9105: a question change shipped in a release follows the same version rules as DHSC's own edits. Seeders,
// feature convergences and guarded UPDATE migrations still change the live form rows; this step, run after them on
// every boot, saves the result as a new version of each PUBLISHED form whose content no longer matches its latest
// version. Then it moves collections' recorded versions (Collection.FollowLatestFormVersions) onto each form's latest:
//
// - Nothing changed: the content hash matches the latest version, so no version is created.
// - A draft (DHSC is editing it) is skipped: its content is unpublished work, which their own publish versions.
// - A form with no version yet is left to FormModuleVersionBackfill, which gives it version 1.
// - BLUM_9087 on: only a collection whose window has not opened moves (Collection.FollowsLatestFormVersions, BLUM-9103),
//   and not one whose open date was moved back after a report started. From its open date on a collection keeps its
//   versions, so a deploy never changes the questions of a window that has opened. The move runs on every boot, not
//   only after a change, so a collection that fell behind (one published before this rule) catches up. Only
//   collections that have not opened are loaded.
// - BLUM_9087 off: the versions are saved all the same, but every report reads the live form, so every collection's
//   recorded version is moved onto its form's latest version. Without that, a release while the flag is off would
//   leave collections on a version older than what their LAs answer, and switching the flag on would hand open
//   reports the old questions. The seeders behave exactly as before either way: this step only reads the forms they
//   wrote.
//
// Each version it saves is audited as data_collection_form.module.version_published with source "release". With the
// flag on, each collection moved gets a collection.form_versions_followed row: source "release" when it moved onto a
// version this boot saved, "catch_up" when it was already behind a version saved earlier. With the flag off, a run
// that moved recorded versions writes one data_collection_form.collection_versions_followed_live row. Like
// FormModuleVersionBackfill it is resolved in ApplicationDbContextInitialiser's boot scope, so it writes and stamps its
// audit rows itself rather than through IAuditLogRepository.
public class FormModuleVersionSync(
    ApplicationDbContext dbContext,
    IFeatureFlags featureFlags,
    ILogger<FormModuleVersionSync> logger,
    IDateTimeProvider dateTimeProvider)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var versions = await dbContext.FormModuleVersions
            .AsNoTracking()
            .Select(v => new LatestVersion(v.Id, v.FormModuleId, v.VersionNumber, v.ContentHash))
            .ToListAsync(cancellationToken);
        var latest = versions
            .GroupBy(v => v.FormModuleId)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.VersionNumber)!);
        if (latest.Count == 0)
        {
            return;
        }

        var modules = await dbContext.FormModules
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.Sections)
            .Include(m => m.Questions)
            .ThenInclude(q => q.Options)
            .ToListAsync(cancellationToken);

        var admin = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Admin, cancellationToken);
        var actor = admin?.Id ?? UserId.Empty();
        var published = modules.Where(m => m.Status.IsPublished && latest.ContainsKey(m.Id)).ToList();
        var saved = 0;

        foreach (var module in published)
        {
            var (definition, hash) = FormModuleDefinition.From(module);
            var current = latest[module.Id];
            if (current.ContentHash == hash)
            {
                continue;
            }

            var utcNow = dateTimeProvider.GetUtcNow();
            var version = FormModuleVersion.Create(FormModuleVersionId.New(), module.Id, current.VersionNumber + 1, definition, hash, actor, utcNow);
            dbContext.FormModuleVersions.Add(version);
            latest[module.Id] = new LatestVersion(version.Id, module.Id, version.VersionNumber, hash);

            var audit = dbContext.AuditLogs.Add(AuditLog.Create(
                actor,
                AuditTargetType.DataCollectionForm,
                module.Id.Value,
                AuditAction.DataCollectionFormModuleVersionPublished,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    moduleId = module.Id.Value,
                    code = module.Code,
                    versionId = version.Id.Value,
                    versionNumber = version.VersionNumber,
                    contentHash = version.ContentHash,
                    source = "release",
                })));
            audit.Property(x => x.OccurredAt).CurrentValue = utcNow;
            saved++;

            logger.LogInformation(
                "Form version sync: form '{Code}' changed in this release; saved version {Version}.",
                module.Code,
                version.VersionNumber);
        }

        if (saved > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var latestVersions = published.ToDictionary(m => m.Id, m => latest[m.Id].Id);
        if (latestVersions.Count == 0)
        {
            return;
        }

        if (featureFlags.IsEnabled(Feature.BLUM_9087))
        {
            var existing = versions.Select(v => v.Id).ToHashSet();
            var savedThisBoot = latest.Values.Where(v => !existing.Contains(v.Id)).Select(v => v.Id).ToHashSet();
            var numbers = versions.Concat(latest.Values).DistinctBy(v => v.Id).ToDictionary(v => v.Id, v => v.VersionNumber);

            await FollowLatestUntilOpenAsync(published, latestVersions, numbers, savedThisBoot, actor, cancellationToken);
        }
        else
        {
            await FollowLiveFormsAsync(published, latest, latestVersions, actor, cancellationToken);
        }
    }

    // Flag on (BLUM-9103): moves the recorded versions of each collection whose window has not opened, judged against
    // one instant for the whole run, onto its forms' latest versions, and audits each collection moved.
    private async Task FollowLatestUntilOpenAsync(
        IReadOnlyList<FormModule> published,
        IReadOnlyDictionary<FormModuleId, FormModuleVersionId> latestVersions,
        IReadOnlyDictionary<FormModuleVersionId, int> versionNumbers,
        IReadOnlySet<FormModuleVersionId> savedThisBoot,
        UserId actor,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();
        var collections = (await BehindTheLatestAsync(latestVersions, DateOnly.FromDateTime(utcNow), cancellationToken))
            .Where(c => c.FollowsLatestFormVersions(utcNow))
            .ToList();
        if (collections.Count == 0)
        {
            return;
        }

        // Whether a report has started is checked under each collection's row lock, which a report start also takes
        // (BLUM-9084), so a window opening during the boot cannot let a report begin on the version being replaced. The
        // lock lasts until the moves commit, so they run in a transaction of their own.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        int moved;
        try
        {
            var ids = collections.Select(c => c.Id).ToList();
            await CollectionRowLocks.ForFormChangesAsync(dbContext, ids, cancellationToken);

            var started = (await dbContext.Submissions
                    .AsNoTracking()
                    .Where(s => ids.Contains(s.CollectionId))
                    .Select(s => s.CollectionId)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var codes = published.ToDictionary(m => m.Id, m => m.Code);
            moved = 0;
            foreach (var collection in collections.Where(c => !started.Contains(c.Id)))
            {
                var moves = collection.FollowLatestFormVersions(latestVersions);
                if (moves.Count == 0)
                {
                    continue;
                }

                var source = moves.Any(m => savedThisBoot.Contains(m.To))
                    ? CollectionFormVersionsFollowedAudit.ReleaseSource
                    : CollectionFormVersionsFollowedAudit.CatchUpSource;
                var audit = dbContext.AuditLogs.Add(CollectionFormVersionsFollowedAudit.Create(
                    actor,
                    collection.Id,
                    moves,
                    versionNumbers,
                    codes,
                    source));
                audit.Property(x => x.OccurredAt).CurrentValue = utcNow;
                moved++;
            }

            if (moved == 0)
            {
                return;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch when (transaction is not null)
        {
            // As in TransactionRunner: the rollback undoes the rows, so the context must not go on tracking them as saved.
            dbContext.ChangeTracker.Clear();
            throw;
        }

        logger.LogInformation(
            "Form version sync: {Moved} collection(s) whose window has not opened now record their forms' latest versions.",
            moved);
    }

    // Flag off: moves every recorded version of a published form onto that form's latest version — which, after the
    // loop in RunAsync, holds the live form.
    private async Task FollowLiveFormsAsync(
        IReadOnlyList<FormModule> published,
        IReadOnlyDictionary<FormModuleId, LatestVersion> latest,
        IReadOnlyDictionary<FormModuleId, FormModuleVersionId> latestVersions,
        UserId actor,
        CancellationToken cancellationToken)
    {
        var collections = await BehindTheLatestAsync(latestVersions, notOpenBy: null, cancellationToken);

        var moved = collections.Sum(c => c.FollowLatestFormVersions(latestVersions).Count);
        if (moved == 0)
        {
            return;
        }

        var audit = dbContext.AuditLogs.Add(AuditLog.Create(
            actor,
            AuditTargetType.System,
            Guid.Empty,
            AuditAction.CollectionFormVersionsFollowedLive,
            before: null,
            after: JsonSerializer.Serialize(new
            {
                collectionFormsMoved = moved,
                versions = published.Select(m => new { moduleId = m.Id.Value, code = m.Code, versionNumber = latest[m.Id].VersionNumber }),
            })));
        audit.Property(x => x.OccurredAt).CurrentValue = dateTimeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Form version sync: form versioning is off, so {Moved} collection form(s) now record their form's latest version.",
            moved);
    }

    // Only collections recording an older version of a published form are loaded, tracked, with their forms. With
    // `notOpenBy`, only published collections whose window had not opened by that day: the SQL half of
    // Collection.FollowsLatestFormVersions (as in CollectionRepository.GetNotYetOpenUsingFormAsync), so past windows,
    // which stay behind for good once a form has a newer version, are not loaded on every boot.
    private async Task<List<Collection>> BehindTheLatestAsync(
        IReadOnlyDictionary<FormModuleId, FormModuleVersionId> latestVersions,
        DateOnly? notOpenBy,
        CancellationToken cancellationToken)
    {
        var moduleIds = latestVersions.Keys.ToList();
        var latestIds = latestVersions.Values.ToList();
        var query = dbContext.Collections
            .Include(c => c.Forms)
            .Where(c => c.Forms.Any(f =>
                f.FormModuleVersionId != null
                && moduleIds.Contains(f.FormModuleId)
                && !latestIds.Contains(f.FormModuleVersionId)));

        if (notOpenBy is { } today)
        {
            query = query.Where(c => !c.IsDraft && !c.IsManuallyClosed && c.CollectionOpenDate != null && c.CollectionOpenDate > today);
        }

        return await query.ToListAsync(cancellationToken);
    }

    private sealed record LatestVersion(FormModuleVersionId Id, FormModuleId FormModuleId, int VersionNumber, string ContentHash);
}