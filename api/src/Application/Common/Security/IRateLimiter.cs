using Application.Common.Settings;

namespace Application.Common.Security;

/// <summary>
/// Counts a request against a partition's budget and says whether it may proceed.
///
/// Synchronous on purpose. The counter is a dictionary lookup and an integer add with nothing to await, and
/// this sits in the pipeline's hot path — making it async would allocate a state machine on every request
/// the service handles in exchange for nothing.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Charges one request to <paramref name="partitionKey"/> and reports the resulting budget.
    ///
    /// Implementations must be safe to call concurrently: two callers arriving together on the same key must
    /// both be counted, or the cap the settings promise is not the cap being applied.
    /// </summary>
    RateLimitDecision Acquire(string partitionKey, RateLimitPolicy policy, RateLimitPolicySettings settings);

    /// <summary>
    /// Drops every counter. Exists for the integration suite, whose counters would otherwise carry from one
    /// test method into the next — one host is shared by a whole test class.
    /// </summary>
    void Clear();
}