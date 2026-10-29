using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentValidation;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Users.Commands.Admins;

public record AdminEditUserCommand : IRequest<Either<UserException, User>>
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Role { get; init; }
    public Guid? OrganisationId { get; init; }
}

public class AdminEditUserCommandHandler(
    IUserRepository repository,
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUserRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService,
    LastActiveAdminGuard lastActiveAdminGuard,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
    : IRequestHandler<AdminEditUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        AdminEditUserCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.Id), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        AdminEditUserCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(request.Id);
        var found = await repository.FindById(userId, cancellationToken);
        return await found.Match<Task<Either<UserException, User>>>(
            user => UpdateUser(request, actorId, user, cancellationToken),
            () => Task.FromResult<Either<UserException, User>>(new UserDoesNotExistException(userId)));
    }

    private async Task<Either<UserException, User>> UpdateUser(
        AdminEditUserCommand request,
        UserId actorId,
        User user,
        CancellationToken cancellationToken)
    {
        var email = request.Email!.Trim().ToLowerInvariant();
        var duplicate = await repository.FindDuplicateAsync(email, user.Id, cancellationToken);
        if (duplicate.IsSome)
        {
            return new DuplicateEmailException(user.Id, email);
        }

        UserRole newRole;
        try
        {
            newRole = UserRole.From(request.Role!);
        }
        catch
        {
            return new UserArgumentException(user.Id, $"Invalid role: {request.Role}");
        }

        var needsOrg = !newRole.Equals(UserRole.Admin);
        if (needsOrg && request.OrganisationId == null)
        {
            return new UserArgumentException(user.Id, "Organisation is required for this role.");
        }

        Organisation? newOrg = null;
        if (needsOrg)
        {
            var orgResult = await organisationRepository.GetOrganisationById(
                new OrganisationId(request.OrganisationId!.Value), cancellationToken);
            if (orgResult.IsNone)
            {
                return new UserOrganisationDoesNotExistException(user.Id, new OrganisationId(request.OrganisationId.Value));
            }

            newOrg = orgResult.ValueUnsafe();
        }

        var lastAdminViolation = await RefuseLastActiveAdminRemoval(user, newRole, newOrg, cancellationToken);
        if (lastAdminViolation is not null)
        {
            return lastAdminViolation;
        }

        return await ApplyUpdate(request, actorId, user, newRole, newOrg, email, cancellationToken);
    }

    // Any edit that stops the target being what they are now is a removal for LastActiveAdminGuard: a DHSC admin
    // losing the Admin role, or an LA Admin being demoted (incl. promoted to DHSC Admin, which strips membership)
    // or moved to another organisation — the vector only this command has, because only DHSC can move a user.
    // So neither DHSC nor an LA is ever left with nobody on Role.UserManagers (BLUM-7038, BLUM-8751).
    private async Task<UserException?> RefuseLastActiveAdminRemoval(
        User user,
        UserRole newRole,
        Organisation? newOrg,
        CancellationToken cancellationToken)
    {
        if (user.Role.Equals(UserRole.Admin))
        {
            return newRole.Equals(UserRole.Admin)
                ? null
                : await lastActiveAdminGuard.RefuseRemovalAsync(user, organisationId: null, cancellationToken);
        }

        if (!user.Role.Equals(UserRole.OrganisationAdmin))
        {
            return null;
        }

        var membership = await organisationUserRepository.FindOrganisationIdForUser(user.Id, cancellationToken);
        var currentOrgId = membership.IsSome ? membership.ValueUnsafe() : null;

        // Nothing is stranded only if the target stays an org admin of the very same organisation.
        var staysSameOrgAdmin = newRole.Equals(UserRole.OrganisationAdmin) && newOrg is not null && newOrg.Id == currentOrgId;
        return staysSameOrgAdmin
            ? null
            : await lastActiveAdminGuard.RefuseRemovalAsync(user, currentOrgId, cancellationToken);
    }

    private async Task<Either<UserException, User>> ApplyUpdate(
        AdminEditUserCommand request,
        UserId actorId,
        User user,
        UserRole newRole,
        Organisation? newOrg,
        string email,
        CancellationToken cancellationToken)
    {
        var before = UserSnapshot.Serialize(user);
        var name = new Name(request.FirstName!, request.LastName!);
        user.UpdateDetails(name, email, dateTimeProvider.GetUtcNow(), newRole);

        var graphResult = await usersService.Update(
            user.SubId.Value.ToString(),
            user.Name,
            user.Email,
            user.PasswordLoginEnabled,
            cancellationToken);

        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(actorId, user, newOrg, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        UserId actorId,
        User user,
        Organisation? newOrg,
        string before,
        CancellationToken cancellationToken)
    {
        // The user, their membership and the audit row commit together (BLUM-8748); the claims cache is dropped
        // once they have.
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    var saved = await repository.UpdateAsync(user, token);

                    await ManageOrganisationMembership(saved, newOrg, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserUpdated, before, UserSnapshot.Serialize(saved)),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(user.Id, exception);
                }
            },
            cancellationToken);

        result.IfRight(saved => userClaimsCache.Invalidate(saved.SubId));
        return result;
    }

    private async Task ManageOrganisationMembership(User user, Organisation? newOrg, CancellationToken cancellationToken)
    {
        var existingOrgUser = await organisationUserRepository.FindByUserIdAsync(user.Id, cancellationToken);

        if (newOrg == null)
        {
            if (existingOrgUser.IsSome)
            {
                await organisationUserRepository.RemoveByUserIdAsync(user.Id, cancellationToken);
            }

            return;
        }

        if (existingOrgUser.IsSome)
        {
            var current = existingOrgUser.ValueUnsafe();
            if (current.OrganisationId == newOrg.Id)
            {
                return;
            }

            await organisationUserRepository.RemoveByUserIdAsync(user.Id, cancellationToken);
        }

        // The id must be the user id, not a fresh guid: every OrganisationUsers command reads one route
        // guid as both the OrganisationUserId and the UserId, and UserDto surfaces the org-user id back
        // to the client as the user id. A synthetic id here left re-homed users permanently un-fetchable
        // and un-editable through the LA users API (BLUM-7038).
        var orgUser = OrganisationUser.New(new OrganisationUserId(user.Id.Value), user.Id, newOrg.Id, user);
        await organisationUserRepository.AddAsync(orgUser, cancellationToken);
    }
}

public class AdminEditUserCommandValidator : AbstractValidator<AdminEditUserCommand>
{
    public AdminEditUserCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(UserRole.IsSupported)
            .WithMessage("Role '{PropertyValue}' is not a supported role.");
    }
}