using Domain.ValueObjects;
using Tests.Data;

namespace Tests.Common;

public sealed record TestIdentity(string Role, Guid UserId, Guid? OrganisationId);

// Ambient test identity shared by TestAuthHandler (HTTP pipeline) and InMemoryIdentityService (Sender path).
// Defaults to the platform admin so existing tests are unaffected; activity tests opt into an LA-user
// identity via BaseIntegrationTest.AuthenticateAs. Backed by AsyncLocal so it flows into the in-process
// TestServer request handling and does not bleed across parallel test classes.
public static class TestIdentityContext
{
    private static readonly AsyncLocal<TestIdentity?> CurrentIdentity = new();

    public static TestIdentity Default { get; } = new(Role.Admin, AdminsData.MainAdmin.Id.Value, null);

    public static TestIdentity Current => CurrentIdentity.Value ?? Default;

    public static void Set(TestIdentity identity) => CurrentIdentity.Value = identity;

    public static void Reset() => CurrentIdentity.Value = null;
}