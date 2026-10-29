using Domain.ValueObjects;
using Tests.Data;

namespace Tests.Common;

/// <summary>
/// The identity TestAuthHandler synthesises, and the one InMemoryIdentityService reports on the Sender path.
/// Status and SubId both default to null, which is the shape every test had before BLUM-6386.
///
/// Status only ever holds <see cref="Domain.ValueObjects.Status.Deactivated"/> in practice, and it changes
/// the whole claim shape rather than adding to it — see TestAuthHandler. SubId is the opposite: it means
/// "synthesise nothing, emit an object id and let production's ApplicationUserClaimsTransformation resolve
/// the rest from the database", so Role, UserId and OrganisationId are ignored when it is set — and reading
/// them anyway is an error rather than a fallback, see InMemoryIdentityService.
/// </summary>
public sealed record TestIdentity(
    string Role,
    Guid UserId,
    Guid? OrganisationId,
    string? Status = null,
    Guid? SubId = null,
    string? Email = null)
{
    public static TestIdentity Deactivated(Guid userId) =>
        new(Domain.ValueObjects.Role.Admin, userId, null, Domain.ValueObjects.Status.Deactivated);

    public static TestIdentity ForSubject(Guid subId) =>
        new(Domain.ValueObjects.Role.Admin, Guid.Empty, null, null, subId);

    public bool IsDeactivated =>
        string.Equals(Status, Domain.ValueObjects.Status.Deactivated, StringComparison.OrdinalIgnoreCase);

    // Every synthesised active identity carries an email, because production's SetUserClaims always emits one
    // and MfaController.Setup answers 401 without it. It cannot be a parameter default (AdminsData is a
    // runtime value), so the default lives here: the main admin's address, which is what the whole suite was
    // getting unconditionally before. A test that asserts on the address passes its own.
    public string EmailOrDefault => Email ?? AdminsData.MainAdmin.Email;
}

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