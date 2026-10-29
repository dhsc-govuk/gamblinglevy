using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.DataCollectionForms;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

// BLUM-9102: add and read only. RepositoryBase's update and remove are not exposed through either interface, and the
// form_module_versions trigger refuses both in the database anyway.
public class FormModuleVersionRepository(ApplicationDbContext dbContext)
    : RepositoryBase<FormModuleVersion, FormModuleVersionId>(dbContext), IFormModuleVersionRepository, IFormModuleVersionQueries
{
    async Task<FormModuleVersion> IFormModuleVersionRepository.AddAsync(
        FormModuleVersion version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Context.FormModuleVersions.AddAsync(version, cancellationToken);
            await Context.SaveChangesAsync(cancellationToken);
            return version;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new FormModuleVersionNumberTakenException(version.FormModuleId, version.VersionNumber);
        }
    }

    public async Task<Option<FormModuleVersion>> GetLatestAsync(
        FormModuleId moduleId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormModuleVersions
            .AsNoTracking()
            .Where(v => v.FormModuleId == moduleId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? Option<FormModuleVersion>.None : Option<FormModuleVersion>.Some(entity);
    }

    public async Task<IReadOnlyDictionary<FormModuleId, FormModuleVersionId>> GetLatestVersionIdsAsync(
        IReadOnlyCollection<FormModuleId> moduleIds,
        CancellationToken cancellationToken = default)
    {
        if (moduleIds.Count == 0)
        {
            return new Dictionary<FormModuleId, FormModuleVersionId>();
        }

        // Grouped in memory: a module has a handful of versions, and only the id, module and number are read.
        var versions = await Context.FormModuleVersions
            .AsNoTracking()
            .Where(v => moduleIds.Contains(v.FormModuleId))
            .Select(v => new { v.Id, v.FormModuleId, v.VersionNumber })
            .ToListAsync(cancellationToken);

        return versions
            .GroupBy(v => v.FormModuleId)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.VersionNumber)!.Id);
    }

    public async Task<IReadOnlyDictionary<FormModuleVersionId, int>> GetVersionNumbersAsync(
        IReadOnlyCollection<FormModuleVersionId> versionIds,
        CancellationToken cancellationToken = default)
    {
        if (versionIds.Count == 0)
        {
            return new Dictionary<FormModuleVersionId, int>();
        }

        return await Context.FormModuleVersions
            .AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<FormModuleVersion>> ListAsync(
        FormModuleId moduleId,
        CancellationToken cancellationToken = default)
    {
        return await Context.FormModuleVersions
            .AsNoTracking()
            .Where(v => v.FormModuleId == moduleId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FormModuleVersionNumberRow>> ListNumbersAsync(
        FormModuleId moduleId,
        CancellationToken cancellationToken = default)
    {
        return await Context.FormModuleVersions
            .AsNoTracking()
            .Where(v => v.FormModuleId == moduleId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new FormModuleVersionNumberRow(v.Id, v.VersionNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<FormModuleVersion>> GetAsync(
        FormModuleId moduleId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormModuleVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.FormModuleId == moduleId && v.VersionNumber == versionNumber, cancellationToken);

        return entity is null ? Option<FormModuleVersion>.None : Option<FormModuleVersion>.Some(entity);
    }

    public async Task<Option<FormModuleVersion>> GetByIdAsync(
        FormModuleVersionId versionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormModuleVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        return entity is null ? Option<FormModuleVersion>.None : Option<FormModuleVersion>.Some(entity);
    }
}