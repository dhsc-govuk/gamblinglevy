namespace Tests.Common;

/// <summary>
/// Lets a test make InMemoryUsersService report a Microsoft Graph failure, or take a measurable amount of
/// time, so the handlers' behaviour when the CIAM directory is unavailable or slow can be asserted rather
/// than assumed.
///
/// AsyncLocal for the same reason as TestIdentityContext: it has to flow into the in-process TestServer
/// (IntegrationTestWebFactory sets PreserveExecutionContext) without bleeding across parallel test classes,
/// which a static bool would do.
/// </summary>
public static class TestGraphContext
{
    private static readonly AsyncLocal<bool> EnableFailsFlag = new();

    private static readonly AsyncLocal<bool> EnablePasswordLoginFailsFlag = new();

    private static readonly AsyncLocal<bool> ResetPasswordFailsFlag = new();

    private static readonly AsyncLocal<Guid?> RecreatedSubIdValue = new();

    private static readonly AsyncLocal<bool> FindByEmailFailsFlag = new();

    private static readonly AsyncLocal<TimeSpan> LookupDelayValue = new();

    private static readonly AsyncLocal<GraphCallGate?> CallGateValue = new();

    public static bool EnableFails => EnableFailsFlag.Value;

    public static bool EnablePasswordLoginFails => EnablePasswordLoginFailsFlag.Value;

    public static bool ResetPasswordFails => ResetPasswordFailsFlag.Value;

    /// <summary>
    /// Gets the object id EnablePasswordLogin answers with when it had to delete and recreate the Entra account, or
    /// null for the usual in-place change. Production does that for an account whose identities Graph will not edit.
    /// </summary>
    public static Guid? RecreatedSubId => RecreatedSubIdValue.Value;

    public static bool FindByEmailFails => FindByEmailFailsFlag.Value;

    /// <summary>
    /// Gets how long each directory call should take. Zero by default, so the rest of the suite is unaffected.
    ///
    /// The bulk importer provisions users through a pool of workers under a wall-clock budget. With the real
    /// Graph replaced by a double that returns instantly, neither the concurrency nor the budget is reachable —
    /// every such test would pass just as well against a plain foreach. This is what gives them something to
    /// measure.
    ///
    /// AsyncLocal is also exactly the right primitive for it, because the value has to flow *into* the pool's
    /// worker tasks, which it does: they are started on the request's execution context.
    /// </summary>
    public static TimeSpan LookupDelay => LookupDelayValue.Value;

    /// <summary>
    /// Gets the gate each directory call must pass through, or null when no test has asked for one.
    /// </summary>
    public static GraphCallGate? CallGate => CallGateValue.Value;

    public static void FailEnable() => EnableFailsFlag.Value = true;

    public static void FailEnablePasswordLogin() => EnablePasswordLoginFailsFlag.Value = true;

    public static void FailResetPassword() => ResetPasswordFailsFlag.Value = true;

    public static void RecreateAccountOnEnablePasswordLogin(Guid newSubId) => RecreatedSubIdValue.Value = newSubId;

    public static void FailFindByEmail() => FindByEmailFailsFlag.Value = true;

    public static void DelayEachCall(TimeSpan delay) => LookupDelayValue.Value = delay;

    /// <summary>
    /// Holds every directory call until <paramref name="parties"/> of them are in flight together.
    ///
    /// A delay makes concurrency measurable only as elapsed time, and elapsed time is a property of the machine:
    /// the test that compared a one-worker run with a six-worker run failed CI at random. This asks the question
    /// directly instead — did that many calls overlap — so use it, not a stopwatch, to assert that work fans out.
    /// The gate object is shared with the worker tasks for the same reason the delay reaches them.
    /// </summary>
    public static GraphCallGate HoldCallsUntilInFlight(int parties, TimeSpan patience)
    {
        var gate = new GraphCallGate(parties, patience);
        CallGateValue.Value = gate;
        return gate;
    }

    public static void Reset()
    {
        CallGateValue.Value = null;
        EnableFailsFlag.Value = false;
        EnablePasswordLoginFailsFlag.Value = false;
        ResetPasswordFailsFlag.Value = false;
        RecreatedSubIdValue.Value = null;
        FindByEmailFailsFlag.Value = false;
        LookupDelayValue.Value = TimeSpan.Zero;
    }
}