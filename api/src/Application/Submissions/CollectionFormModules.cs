using Application.Common.Features;
using Application.Common.Interfaces.Queries;
using Application.DataCollectionForms.Versions;
using Domain.Collections;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// BLUM-9104: the one place a quarterly report gets a form module's questions. With BLUM_9087 on, a report reads the
// version its collection recorded (BLUM-9103), so a question DHSC changes afterwards does not reach it: two collections
// open at once each see their own version. The module comes back as a detached FormModule built from that version
// (FormModuleDefinition.ToModule), so every reader typed on the live entities reads it unchanged.
//
// It falls back to the live module whenever there is no version to read: the flag is off (today's behaviour exactly),
// no collection is given (the activity-record export, which spans many reports), the collection does not use the
// module, or it records no version (a draft, or a collection the backfill has not reached yet).
public class CollectionFormModules(IFormModuleQueries formModuleQueries, IFormModuleVersionQueries versionQueries, IFeatureFlags featureFlags)
{
    public async Task<Option<FormModule>> GetAsync(Collection? collection, string moduleCode, CancellationToken cancellationToken)
    {
        var lookup = await formModuleQueries.GetByCodeAsync(moduleCode, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<FormModule>.None;
        }

        var moduleId = lookup.ValueUnsafe().Id;
        var recorded = collection is not null && featureFlags.IsEnabled(Feature.BLUM_9087)
            ? collection.Forms.FirstOrDefault(form => form.FormModuleId == moduleId)?.FormModuleVersionId
            : null;
        if (recorded is not null)
        {
            var version = await versionQueries.GetByIdAsync(recorded, cancellationToken);
            if (version.IsSome)
            {
                return FormModuleDefinition.ToModule(version.ValueUnsafe());
            }
        }

        return await formModuleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
    }
}