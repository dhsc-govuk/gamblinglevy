using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.DataCollectionForms.Versions;
using Domain.DataCollectionForms;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class DataCollectionFormsControllerService(
    IFormModuleQueries formModuleQueries,
    IFormModuleVersionQueries formModuleVersionQueries,
    ICollectionQueries collectionQueries,
    IUserQueries userQueries,
    IDateTimeProvider dateTimeProvider)
    : IDataCollectionFormsControllerService
{
    public FormModuleWithChildrenDto ToDto(FormModule module) =>
        FormModuleWithChildrenDto.FromDomainModel(module);

    public async Task<IReadOnlyList<FormModuleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var modules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        return modules
            .OrderBy(m => m.DisplayOrder)
            .Select(FormModuleDto.FromDomainModel)
            .ToList();
    }

    public async Task<IReadOnlyList<FormModuleVersionSummaryDto>> ListVersionsAsync(FormModuleId moduleId, CancellationToken cancellationToken)
    {
        var versions = await formModuleVersionQueries.ListAsync(moduleId, cancellationToken);
        var names = await PublisherNamesAsync(versions.Select(v => v.PublishedBy), cancellationToken);
        return versions
            .Select(v => FormModuleVersionSummaryDto.FromDomainModel(v, names.GetValueOrDefault(v.PublishedBy)))
            .ToList();
    }

    public async Task<IReadOnlyList<FormModuleVersionCollectionsDto>> ListVersionCollectionsAsync(FormModuleId moduleId, CancellationToken cancellationToken)
    {
        // Collections first. Versions are never deleted, so every version a collection records is in the list read after
        // it. Read the other way round, a publish between the two reads could move a collection onto a version the list
        // doesn't hold, and the collection would drop out of the response.
        var collections = await collectionQueries.GetUsingFormVersionsAsync(moduleId, cancellationToken);
        var versions = await formModuleVersionQueries.ListNumbersAsync(moduleId, cancellationToken);

        // One instant for every status in the response. Each collection holds only its row for this module, and that row
        // records a version.
        var utcNow = dateTimeProvider.GetUtcNow();
        var byVersion = collections
            .GroupBy(c => c.Forms.Single().FormModuleVersionId!)
            .ToDictionary(g => g.Key, g => g.Select(c => CollectionSummaryDto.FromDomainModel(c, utcNow)).ToList());

        return versions
            .Select(v => new FormModuleVersionCollectionsDto(
                v.Id.Value,
                v.VersionNumber,
                byVersion.GetValueOrDefault(v.Id) ?? []))
            .ToList();
    }

    public async Task<Option<FormModuleVersionDto>> GetVersionAsync(FormModuleId moduleId, int versionNumber, CancellationToken cancellationToken)
    {
        var lookup = await formModuleVersionQueries.GetAsync(moduleId, versionNumber, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<FormModuleVersionDto>.None;
        }

        var version = lookup.ValueUnsafe();
        var names = await PublisherNamesAsync([version.PublishedBy], cancellationToken);
        return FormModuleVersionDto.FromDomainModel(version, FormModuleDefinition.Parse(version.Definition), names.GetValueOrDefault(version.PublishedBy));
    }

    // A handful of DHSC admins publish forms, so one lookup per distinct publisher is cheap.
    private async Task<Dictionary<UserId, string>> PublisherNamesAsync(IEnumerable<UserId> publishers, CancellationToken cancellationToken)
    {
        var names = new Dictionary<UserId, string>();
        foreach (var publisher in publishers.Distinct())
        {
            var user = await userQueries.FindById(publisher, cancellationToken);
            if (user.IsSome)
            {
                names[publisher] = user.ValueUnsafe().Name.ToString();
            }
        }

        return names;
    }
}