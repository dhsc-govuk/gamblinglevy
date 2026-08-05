using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Domain.AuditLogs;
using Domain.OrganisationUsers;
using Domain.Users;
using FluentValidation;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Users.Commands.OrganisationUsers;

public record ResetOrganisationUserMfaCommand(Guid OrganisationId, Guid UserId)
    : IRequest<Either<UserException, User>>;

public class ResetOrganisationUserMfaCommandHandler(
    PermissionsService permissionsService,
    IOrganisationUserRepository organisationUserRepository,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache)
    : IRequestHandler<ResetOrganisationUserMfaCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ResetOrganisationUserMfaCommand request,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();
        var organisationUserId = new OrganisationUserId(request.UserId);
        var targetUserId = new UserId(request.UserId);

        return await permissions.MatchAsync(
            p => actorIdResult.MatchAsync<Either<UserException, User>>(
                actorId => organisationUserRepository.GetOrganisationUserById(organisationUserId, p, cancellationToken)
                    .MatchAsync(
                        orgUser => ResetMfa(orgUser, actorId, cancellationToken),
                        () => new UserDoesNotExistException(targetUserId)),
                e => new UserUnknownException(targetUserId, e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, User>> ResetMfa(
        OrganisationUser orgUser,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = orgUser.User!;
            var before = UserSnapshot.Serialize(user);

            var mfa = await userMfaRepository.GetByUserIdAsync(user.Id, cancellationToken);
            if (mfa.IsSome)
            {
                await userMfaRepository.DeleteAsync(mfa.ValueUnsafe(), cancellationToken);
            }

            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
            userClaimsCache.Invalidate(user.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, user.Id.Value, AuditAction.UserMfaReset, before, null),
                cancellationToken);

            return user;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(orgUser.User!.Id, exception);
        }
    }
}

public class ResetOrganisationUserMfaCommandValidator : AbstractValidator<ResetOrganisationUserMfaCommand>
{
    public ResetOrganisationUserMfaCommandValidator()
    {
        RuleFor(x => x.OrganisationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}