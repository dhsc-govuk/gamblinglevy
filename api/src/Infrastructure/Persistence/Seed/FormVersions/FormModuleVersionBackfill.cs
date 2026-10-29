using System.Text.Json;
using Application.Common.Interfaces;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using Domain.Users;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seed.FormVersions;

// BLUM-9103: gives every published form module a version 1 and every published collection a version for each of its
// forms.
//
// Forms published before versioning existed (BLUM-9102) have no version, and collections published before BLUM-9103
// record none. This step fills both gaps from what is there now: version 1 of a form is its current questions, and a
// published collection's unrecorded form is linked to that form's latest version — on the first run, version 1. So
// nothing an LA sees changes: the questions version 1 holds are the ones they already see.
//
// It is deliberately NOT behind BLUM_9087 and runs on every boot, but only ever fills gaps, so after the first run it
// does nothing. It is a boot step rather than a migration because a version's content hash is computed in C#
// (FormModuleDefinition) and must match what a later publish computes. A draft collection is left unrecorded on
// purpose: it takes its versions when it is published.
//
// Linking collections writes one data_collection_form.versions_backfilled audit row (see RunAsync for why creating
// version 1 alone does not). It lists each collection it linked with the version each of those forms now records
// (BLUM-9106's second criterion, BLUM-9084). Like MaturityBaselineSeeder it is resolved inside
// ApplicationDbContextInitialiser's boot scope, so it writes its audit row straight to the context and stamps it itself
// rather than going through IAuditLogRepository.
public class FormModuleVersionBackfill(ApplicationDbContext dbContext, ILogger<FormModuleVersionBackfill> logger, IDateTimeProvider dateTimeProvider)
{
    // The audit row's payload version: 2 added `collections`, each collection linked with its forms' versions
    // ({ collectionId, formVersions: [{ moduleId, code, versionNumber }] }, the entry shape of the collection rows).
    // Version 1 rows, written before it, carry only versionsCreated and the collectionFormsLinked count.
    public const int CollectionsListedPayloadVersion = 2;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var versionedModuleIds = await dbContext.FormModuleVersions
            .Select(v => v.FormModuleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var unversioned = await dbContext.FormModules
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.Sections)
            .Include(m => m.Questions)
            .ThenInclude(q => q.Options)
            // Ever published, not "is Published": a draft DHSC is still building has no published content to keep, and
            // a version of it would be offered to collections and would fix its codes and option values (BLUM-9105).
            // Its first publish saves its version 1. A reopened draft already has a version, so it is not here.
            .Where(m => !versionedModuleIds.Contains(m.Id) && m.PublishedAt != null)
            .ToListAsync(cancellationToken);

        var created = new List<object>();
        foreach (var module in unversioned)
        {
            var (definition, hash) = FormModuleDefinition.From(module);
            var version = FormModuleVersion.Create(
                FormModuleVersionId.New(),
                module.Id,
                1,
                definition,
                hash,
                module.PublishedBy ?? module.CreatedBy,
                module.PublishedAt ?? dateTimeProvider.GetUtcNow());
            dbContext.FormModuleVersions.Add(version);
            created.Add(new { moduleId = module.Id.Value, code = module.Code, versionId = version.Id.Value, versionNumber = 1 });
        }

        if (created.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Only a form that has a version can be linked. A form with none — a maturity catalogue section, or a form never
        // published (funding_spent) — stays unrecorded for good, so matching on it alone would reload every published
        // collection on every boot to change nothing.
        var versionedIds = versionedModuleIds.Concat(unversioned.Select(m => m.Id)).ToList();
        var collections = await dbContext.Collections
            .Include(c => c.Forms)
            .Where(c => !c.IsDraft && c.Forms.Any(f => f.FormModuleVersionId == null && versionedIds.Contains(f.FormModuleId)))
            .ToListAsync(cancellationToken);

        var linked = 0;
        var linkedCollections = new List<object>();
        if (collections.Count > 0)
        {
            var moduleIds = collections.SelectMany(c => c.Forms).Select(f => f.FormModuleId).Distinct().ToList();
            var versions = await dbContext.FormModuleVersions
                .AsNoTracking()
                .Where(v => moduleIds.Contains(v.FormModuleId))
                .Select(v => new { v.Id, v.FormModuleId, v.VersionNumber })
                .ToListAsync(cancellationToken);
            var latest = versions
                .GroupBy(v => v.FormModuleId)
                .ToDictionary(g => g.Key, g => g.MaxBy(v => v.VersionNumber)!.Id);
            var numbers = versions.ToDictionary(v => v.Id, v => v.VersionNumber);
            var codes = await dbContext.FormModules
                .AsNoTracking()
                .Where(m => moduleIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Code, cancellationToken);

            foreach (var collection in collections)
            {
                var unrecorded = collection.Forms.Where(f => f.FormModuleVersionId is null).ToList();
                collection.PinFormVersions(latest);

                var pinned = unrecorded.Where(f => f.FormModuleVersionId is not null).ToList();
                if (pinned.Count == 0)
                {
                    continue;
                }

                linked += pinned.Count;
                linkedCollections.Add(new
                {
                    collectionId = collection.Id.Value,
                    formVersions = pinned.Select(f => new
                    {
                        moduleId = f.FormModuleId.Value,
                        code = codes.GetValueOrDefault(f.FormModuleId),
                        versionNumber = numbers[f.FormModuleVersionId!],
                    }),
                });
            }
        }

        if (created.Count == 0 && linked == 0)
        {
            return;
        }

        logger.LogInformation(
            "Form version backfill: created version 1 of {Versions} form(s) and recorded versions for {Linked} collection form(s).",
            created.Count,
            linked);

        // Only linking existing collections is audited: it changes records people already have, and on the first deploy
        // the row also lists every version 1 created alongside. A version 1 created on a fresh or reset database, with no
        // collections yet, is ordinary seeding, which the module seeders do not audit either.
        if (linked == 0)
        {
            return;
        }

        var admin = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Admin, cancellationToken);
        var audit = dbContext.AuditLogs.Add(AuditLog.Create(
            admin?.Id ?? UserId.Empty(),
            AuditTargetType.System,
            Guid.Empty,
            AuditAction.FormModuleVersionsBackfilled,
            before: null,
            after: JsonSerializer.Serialize(new { versionsCreated = created, collectionFormsLinked = linked, collections = linkedCollections }),
            payloadVersion: CollectionsListedPayloadVersion));
        audit.Property(x => x.OccurredAt).CurrentValue = dateTimeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}