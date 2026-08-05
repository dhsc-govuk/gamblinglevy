using Application.Common.Interfaces;
using Domain;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class UserProfileRepository(ApplicationDbContext dbContext) : IUserProfileRepository, IAsyncDisposable
{
    public async Task<Option<UserProfile>> GetUserProfile(SubId subId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext
            .Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubId.Equals(subId), cancellationToken);

        if (user != null)
        {
            return await GetUserProfile(user, cancellationToken);
        }

        return Option<UserProfile>.None;
    }

    public ValueTask DisposeAsync()
    {
        return dbContext.DisposeAsync();
    }

    private async Task<Option<UserProfile>> GetUserProfile(
        User user,
        CancellationToken cancellationToken = default)
    {
        if (user.Role.Equals(UserRole.Admin))
        {
            return UserProfile.NewAdminProfile(user);
        }

        // Every LA role resolves through organisation_users. Asking the role rather than listing roles is
        // what keeps this in step with Role.AnyOrganisationMember: the [Authorize] lists let Lead through
        // while this method did not, so Leads authenticated and then got no claims at all, which made
        // every [Authorize(Roles = ...)] reject them (BLUM-7038).
        if (user.Role.IsOrganisationMember())
        {
            var member = await dbContext.OrganisationUsers
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Organisation)
                .SingleOrDefaultAsync(x => x.UserId.Equals(user.Id), cancellationToken);

            return member is null
                ? Option<UserProfile>.None
                : UserProfile.NewOrganisationMemberProfile(member);
        }

        return Option<UserProfile>.None;
    }
}