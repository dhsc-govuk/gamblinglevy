using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentValidation;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Users.Commands.OrganisationUsers;

public record DeleteOrganisationUserCommand(Guid OrganisationId, Guid UserId)
    : IRequest<Either<UserException, Unit>>;

public class DeleteOrganisationUserCommandHandler(
    PermissionsService permissionsService,
    IOrganisationUserRepository organisationUserRepository,
    IUserRepository usersRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache)
    : IRequestHandler<DeleteOrganisationUserCommand, Either<UserException, Unit>>
{
    public async Task<Either<UserException, Unit>> Handle(
        DeleteOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();
        var targetUserId = new UserId(request.UserId);
        var organisationUserId = new OrganisationUserId(request.UserId);

        return await permissions.MatchAsync(
            p => actorIdResult.MatchAsync<Either<UserException, Unit>>(
                actorId => organisationUserRepository.GetOrganisationUserById(organisationUserId, p, cancellationToken)
                    .MatchAsync(
                        orgUser => ValidateAndDelete(orgUser, actorId, cancellationToken),
                        () => new UserDoesNotExistException(targetUserId)),
                e => new UserUnknownException(targetUserId, e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, Unit>> ValidateAndDelete(
        OrganisationUser orgUser,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var user = orgUser.User!;

        if (user.Id == actorId)
        {
            return new CannotDeleteSelfException(user.Id);
        }

        if (user.Role.Equals(UserRole.OrganisationAdmin) && user.Status.Equals(UserStatus.Active))
        {
            var admins = await organisationUserRepository.GetActiveOrgAdminsAsync(orgUser.OrganisationId, cancellationToken);
            if (admins.Count == 1 && admins[0].Id == user.Id)
            {
                return new CannotDeactivateOnlyOrgAdminException(user.Id, orgUser.OrganisationId);
            }
        }

        var before = UserSnapshot.Serialize(user);

        var graphResult = await usersService.HardDelete(user.SubId.Value.ToString(), cancellationToken);
        return await graphResult.MatchAsync<Either<UserException, Unit>>(
            _ => RemoveAndAudit(user, orgUser, actorId, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, Unit>> RemoveAndAudit(
        User user,
        OrganisationUser orgUser,
        UserId actorId,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            await organisationUserRepository.RemoveAsync(orgUser, cancellationToken);
            await usersRepository.RemoveAsync(user, cancellationToken);
            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
            userClaimsCache.Invalidate(user.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.User,
                    user.Id.Value,
                    AuditAction.UserDeleted,
                    before,
                    after: null,
                    organisationId: orgUser.OrganisationId),
                cancellationToken);

            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class DeleteOrganisationUserCommandValidator : AbstractValidator<DeleteOrganisationUserCommand>
{
    public DeleteOrganisationUserCommandValidator()
    {
        RuleFor(x => x.OrganisationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}