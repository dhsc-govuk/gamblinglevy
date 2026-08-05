using Application.Common.Interfaces;
using Domain.Users;

namespace Infrastructure.Caching;

public class UserClaimsCache(IInMemoryCache cache) : IUserClaimsCache
{
    public void Invalidate(SubId subId)
    {
        cache.Invalidate($"user::{subId.Value}");
    }
}