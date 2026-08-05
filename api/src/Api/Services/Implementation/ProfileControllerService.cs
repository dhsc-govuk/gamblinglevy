using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Mfa.Dtos;
using Domain;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class ProfileControllerService(
    IIdentityService identityService,
    IInMemoryCache inMemoryCache,
    IMfaSessionQueries mfaSessionQueries) : IProfileControllerService
{
    public Option<Profile> GetMe()
    {
        var subId = identityService.GetSubId();
        return subId.Match(
            s => inMemoryCache.GetItem<UserProfile>($"user::{s}").Match(
                cachedUser => new Profile(
                    Id: cachedUser.Id!.Value,
                    FirstName: cachedUser.Name!.FirstName,
                    LastName: cachedUser.Name.LastName,
                    Email: cachedUser.Email!,
                    Status: cachedUser.Status!.ToString(),
                    Role: cachedUser.Role!.ToString(),
                    UpdatedAt: cachedUser.UpdatedAt,
                    OrganisationId: cachedUser.OrganisationId.ValueUnsafe()?.Value,
                    LocationId: cachedUser.LocationId.ValueUnsafe()?.Value),
                () => Option<Profile>.None),
            () => Option<Profile>.None);
    }

    public async Task<IReadOnlyList<MfaSessionListItemDto>> GetMySessionsAsync(
        UserId userId,
        MfaSessionId? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await mfaSessionQueries.GetActiveForUserAsync(userId, cancellationToken);

        // Marking the caller's own row is what makes the list actionable: without it, a user deciding which
        // session to end has no way to avoid signing themselves out of the browser they are reading it in.
        return sessions
            .Select(session => session with
            {
                IsCurrent = currentSessionId is not null && session.Id == currentSessionId.Value,
            })
            .ToList();
    }
}