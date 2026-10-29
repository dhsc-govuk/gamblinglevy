using Application.Users.Exceptions;
using Domain.Users;
using Domain.ValueObjects;
using FluentValidation;
using LanguageExt;
using MediatR;

namespace Application.Users.Commands.DeactivateUser;

// Sent by both admin/users/{id}/deactivate and organisations/{organisationId}/users/{id}/deactivate: who the
// caller may reach is UserTargetResolver's to decide, from their claims.
public record DeactivateUserCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class DeactivateUserCommandHandler(
    UserTargetResolver userTargetResolver,
    LastActiveAdminGuard lastActiveAdminGuard,
    UserDeactivator userDeactivator)
    : IRequestHandler<DeactivateUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        DeactivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => ValidateAndDeactivate(t, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> ValidateAndDeactivate(
        UserTarget target,
        CancellationToken cancellationToken)
    {
        var user = target.User;

        if (user.Status.Equals(UserStatus.Deactivated))
        {
            return new UserAlreadyDeactivatedException(user.Id);
        }

        if (user.Id == target.ActorId)
        {
            return new CannotDeactivateSelfException(user.Id);
        }

        var refusal = await lastActiveAdminGuard.RefuseRemovalAsync(user, target.OrganisationId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        try
        {
            return await userDeactivator.DeactivateAsync(user, target.ActorId, target.OrganisationId);
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}