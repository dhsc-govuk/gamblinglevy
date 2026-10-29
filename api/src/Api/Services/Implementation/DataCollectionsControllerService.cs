using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class DataCollectionsControllerService(
    ICollectionQueries collectionQueries,
    IFormModuleQueries formModuleQueries,
    IFormModuleVersionQueries formModuleVersionQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries,
    IDateTimeProvider dateTimeProvider) : IDataCollectionsControllerService
{
    public async Task<CollectionDto> ToDtoAsync(Collection collection, CancellationToken cancellationToken)
    {
        var laNames = await GetLocalAuthorityNamesAsync(cancellationToken);
        var modules = await GetModuleLookupAsync(cancellationToken);
        var versionNumbers = await formModuleVersionQueries.GetVersionNumbersAsync(
            collection.Forms.Where(f => f.FormModuleVersionId is not null).Select(f => f.FormModuleVersionId!).ToList(),
            cancellationToken);

        var forms = collection.Forms
            .Select(f =>
            {
                var module = ResolveForm(modules, f.FormModuleId.Value);
                int? versionNumber = f.FormModuleVersionId is { } versionId && versionNumbers.TryGetValue(versionId, out var number) ? number : null;
                return new CollectionFormDto(f.FormModuleId.Value, module?.Code, module?.Name, versionNumber);
            })
            .ToList();

        var localAuthorities = collection.LocalAuthorities
            .Select(la => new CollectionLocalAuthorityDto(
                la.LocalAuthorityId.Value,
                ResolveName(laNames, la.LocalAuthorityId.Value)))
            .ToList();

        var reminders = collection.Reminders
            .Select(r => new CollectionReminderDto(r.OffsetType.ToString(), r.Days))
            .ToList();

        var createdByName = await ResolveUserNameAsync(collection.CreatedBy, cancellationToken);
        var updatedByName = await ResolveUserNameAsync(collection.UpdatedBy, cancellationToken);

        return new CollectionDto(
            collection.Id.Value,
            collection.Name,
            collection.Purpose,
            collection.Type?.ToString(),
            collection.FinancialYear,
            collection.Quarter,
            collection.ReportingPeriodStart,
            collection.ReportingPeriodEnd,
            collection.WindowSameAsReportingPeriod,
            collection.CollectionOpenDate,
            collection.SubmissionDeadline,
            collection.IsSubmittedByAllLas,
            forms,
            localAuthorities,
            reminders,
            collection.GetStatus(dateTimeProvider.GetUtcNow()).ToString(),
            collection.IsDraft,
            collection.CreatedAt,
            collection.CreatedBy?.Value,
            createdByName,
            collection.UpdatedAt,
            collection.UpdatedBy?.Value,
            updatedByName);
    }

    public async Task<IReadOnlyList<CollectionSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var collections = await collectionQueries.GetAllAsync(cancellationToken);
        return ToSummaries(collections, dateTimeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<CollectionSummaryDto>> DeletedAsync(CancellationToken cancellationToken)
    {
        var collections = await collectionQueries.GetDeletedAsync(cancellationToken);
        return ToSummaries(collections, dateTimeProvider.GetUtcNow());
    }

    public async Task<Option<CollectionDto>> GetAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<CollectionDto>.None;
        }

        return await ToDtoAsync(lookup.ValueUnsafe(), cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionFormOptionDto>> GetFormOptionsAsync(CancellationToken cancellationToken)
    {
        var modules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        // BLUM-9103: each form module's saved versions, newest first, for choosing one per collection. A few modules
        // with a handful of versions each, so one read per module is cheap.
        var quarterly = new List<CollectionFormOptionDto>();
        foreach (var module in modules)
        {
            var versions = await formModuleVersionQueries.ListAsync(module.Id, cancellationToken);
            quarterly.Add(new CollectionFormOptionDto(
                module.Id.Value,
                module.Name,
                module.UpdatedAt ?? module.CreatedAt,
                CollectionTypes.QuarterlyReport,
                versions.Select(v => new CollectionFormVersionOptionDto(v.VersionNumber, v.PublishedAt)).ToList()));
        }

        var maturity = MaturityCollectionForms.All
            .Select(f => new CollectionFormOptionDto(f.Id, f.Title, null, CollectionTypes.MaturityAssessment));
        return quarterly.Concat(maturity).ToList();
    }

    public async Task<IReadOnlyList<ReportingLaDto>> GetReportingLasAsync(CancellationToken cancellationToken)
    {
        var localAuthorities = await organisationQueries.GetLocalAuthorities(null, cancellationToken);
        return localAuthorities
            .Select(la => new ReportingLaDto(la.Id.Value, la.Name))
            .ToList();
    }

    private static IReadOnlyList<CollectionSummaryDto> ToSummaries(IReadOnlyList<Collection> collections, DateTime utcNow)
    {
        return collections
            .Select(c => CollectionSummaryDto.FromDomainModel(c, utcNow))
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> GetLocalAuthorityNamesAsync(CancellationToken cancellationToken)
    {
        var localAuthorities = await organisationQueries.GetLocalAuthorities(null, cancellationToken);
        return localAuthorities.ToDictionary(la => la.Id.Value, la => la.Name);
    }

    private async Task<Dictionary<Guid, (string Code, string Name)>> GetModuleLookupAsync(CancellationToken cancellationToken)
    {
        var modules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        return modules.ToDictionary(m => m.Id.Value, m => (m.Code, m.Name));
    }

    private async Task<string?> ResolveUserNameAsync(UserId? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return null;
        }

        var lookup = await userQueries.FindById(userId, cancellationToken);
        return lookup.IsSome ? lookup.ValueUnsafe().Name.ToString() : null;
    }

    private static string? ResolveName(IReadOnlyDictionary<Guid, string> lookup, Guid id)
    {
        return lookup.TryGetValue(id, out var name) ? name : null;
    }

    // A collection's form id is either a maturity catalogue section (BLUM-7943) or a form module.
    private static (string Code, string Name)? ResolveForm(
        IReadOnlyDictionary<Guid, (string Code, string Name)> lookup,
        Guid id)
    {
        var section = MaturityCollectionForms.Find(id);
        if (section is not null)
        {
            return (section.SectionCode, section.Title);
        }

        return lookup.TryGetValue(id, out var module) ? module : null;
    }
}