using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;

namespace Application.Users.Commands.Admins;

public record UpdateAdminCommand : IRequest<Either<UserException, User>>
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
}

public class UpdateAdminCommandHandler(
    IUserRepository repository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateAdminCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        UpdateAdminCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.Id), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        UpdateAdminCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var userId = new UserId(request.Id);
        var userResult = await GetUser(userId, cancellationToken);
        return await userResult.BindAsync(user => UpdateUser(request, actorId, user, cancellationToken));
    }

    private async Task<Either<UserException, User>> GetUser(
        UserId id,
        CancellationToken cancellationToken)
    {
        var userById = await repository.FindById(id, cancellationToken);
        return userById.Match<Either<UserException, User>>(
            user => user.Role.Equals(UserRole.Admin)
                ? user
                : new UserWrongRoleException(user.Id, UserRole.Admin),
            new UserDoesNotExistException(id));
    }

    private async Task<Either<UserException, User>> UpdateUser(
        UpdateAdminCommand request,
        UserId actorId,
        User user,
        CancellationToken cancellationToken)
    {
        var email = request.Email!.Trim();
        var name = new Name(request.FirstName!, request.LastName!);

        var duplicate = await repository.FindDuplicateAsync(email, user.Id, cancellationToken);
        return await duplicate.MatchAsync(
            u => new DuplicateEmailException(u.Id, u.Email),
            () => ApplyUpdate(actorId, user, name, email, cancellationToken));
    }

    private async Task<Either<UserException, User>> ApplyUpdate(
        UserId actorId,
        User user,
        Name name,
        string email,
        CancellationToken cancellationToken)
    {
        var before = UserSnapshot.Serialize(user);

        user.UpdateDetails(name, email);

        var graphResult = await usersService.Update(
            user.SubId.Value.ToString(),
            user.Name,
            user.Email,
            cancellationToken);

        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(actorId, user, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        UserId actorId,
        User user,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await repository.UpdateAsync(user, cancellationToken);
            userClaimsCache.Invalidate(saved.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserUpdated, before, UserSnapshot.Serialize(saved)),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}