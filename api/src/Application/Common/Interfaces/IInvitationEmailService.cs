using Domain.ValueObjects;
using LanguageExt;

namespace Application.Common.Interfaces;

public interface IInvitationEmailService
{
    Task<Either<Exception, Unit>> SendInvitationAsync(string email, Name name, CancellationToken cancellationToken);
}