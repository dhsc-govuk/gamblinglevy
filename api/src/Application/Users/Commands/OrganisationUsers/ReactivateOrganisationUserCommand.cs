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

namespace Application.Users.Commands.OrganisationUsers;

public record ReactivateOrganisationUserCommand(Guid OrganisationId, Guid UserId)
    : IRequest<Either<UserException, User>>;

public class ReactivateOrganisationUserCommandHandler(
    PermissionsService permissionsService,
    IOrganisationUserRepository organisationUserRepository,
    IUserRepository usersRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache)
    : IRequestHandler<ReactivateOrganisationUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ReactivateOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();
        var targetUserId = new UserId(request.UserId);
        var organisationUserId = new OrganisationUserId(request.UserId);

        return await permissions.MatchAsync(
            p => actorIdResult.MatchAsync<Either<UserException, User>>(
                actorId => organisationUserRepository.GetOrganisationUserById(organisationUserId, p, cancellationToken)
                    .MatchAsync(
                        orgUser => ValidateAndReactivate(orgUser, actorId, cancellationToken),
                        () => new UserDoesNotExistException(targetUserId)),
                e => new UserUnknownException(targetUserId, e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, User>> ValidateAndReactivate(
        OrganisationUser orgUser,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var user = orgUser.User!;

        if (user.Status.Equals(UserStatus.Active))
        {
            return new UserAlreadyActiveException(user.Id);
        }

        var before = UserSnapshot.Serialize(user);
        user.Reactivate();

        var graphResult = await usersService.Enable(user.SubId.Value.ToString(), cancellationToken);
        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(user, actorId, orgUser.OrganisationId, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        User user,
        UserId actorId,
        Domain.Organisations.OrganisationId organisationId,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await usersRepository.UpdateAsync(user, cancellationToken);
            userClaimsCache.Invalidate(saved.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.User,
                    saved.Id.Value,
                    AuditAction.UserReactivated,
                    before,
                    UserSnapshot.Serialize(saved),
                    organisationId: organisationId),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class ReactivateOrganisationUserCommandValidator : AbstractValidator<ReactivateOrganisationUserCommand>
{
    public ReactivateOrganisationUserCommandValidator()
    {
        RuleFor(x => x.OrganisationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}