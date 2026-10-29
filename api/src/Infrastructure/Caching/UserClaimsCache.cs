using Application.Common.Interfaces;
using Domain.Users;

namespace Infrastructure.Caching;

/// <summary>
/// Drops the cached UserProfile that ApplicationUserClaimsTransformation reads, so the next request
/// re-reads the user's role and status from the database instead of serving a 5-minute-old copy.
///
/// This is process-local: IInMemoryCache wraps IMemoryCache, so an invalidation only reaches the instance
/// that handled the request. Since BLUM-6386 that matters more than it used to — deactivation no longer
/// disables the CIAM account, so this cache is the only thing standing between a just-deactivated user and
/// the service. On a single instance the invalidation is immediate and complete, which is what the API runs
/// on today (the Terraform service plan sets no worker_count and there is no autoscale rule). Scaling out
/// would open a window of up to the 5-minute TTL on every other instance, during which a deactivated user
/// still holds a live Entra token and would still be served. Scaling out therefore needs a shared cache, or
/// a status re-read that does not go through this cache — it is not a configuration change on its own.
/// </summary>
public class UserClaimsCache(IInMemoryCache cache) : IUserClaimsCache
{
    public void Invalidate(SubId subId)
    {
        cache.Invalidate($"user::{subId.Value}");
    }
}