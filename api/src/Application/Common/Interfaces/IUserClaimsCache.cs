using Domain.Users;

namespace Application.Common.Interfaces;

public interface IUserClaimsCache
{
    void Invalidate(SubId subId);
}