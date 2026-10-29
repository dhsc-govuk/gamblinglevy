using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Organisations;
using Domain.ValueObjects;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Tests.Data;
using Xunit;

namespace Tests.Common;

public abstract class BaseIntegrationTest : IClassFixture<IntegrationTestWebFactory>
{
    protected readonly ISender Sender;
    protected readonly ApplicationDbContext Context;
    protected readonly HttpClient Client;

    protected BaseIntegrationTest(IntegrationTestWebFactory factory)
    {
        var scope = factory.Services.CreateScope();

        Sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Deliberately NOT factory.WithWebHostBuilder(...): this constructor runs once per test, and each
        // WithWebHostBuilder call mints a new WebApplicationFactory whose host runs the real entry point,
        // re-running MigrateAsync and every seeder against the same container. The TestScheme registration
        // that used to live here now sits in IntegrationTestWebFactory.ConfigureWebHost, so the class fixture
        // boots exactly one host and CreateClient below just reuses it.
        //
        // Flow the ambient TestIdentityContext (AsyncLocal) from the test into the in-process server
        // pipeline so TestAuthHandler and InMemoryIdentityService observe the same identity.
        factory.Server.PreserveExecutionContext = true;

        Client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(scheme: "TestScheme");
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("IntegrationTest/1.0");
    }

    // Switches the ambient identity (HTTP + Sender) to an organisation-scoped user for the current test.
    // The email is synthesised like every other claim and defaults to the main admin's, which is what the
    // whole suite received before it was a claim at all — pass one when the test asserts on the address
    // (MfaController.Setup is the only endpoint that reads it).
    protected static void AuthenticateAs(string role, Guid userId, Guid organisationId, string? email = null)
    {
        TestIdentityContext.Set(new TestIdentity(role, userId, organisationId, Email: email));
    }

    // A deactivated user, in the claim shape ApplicationUserClaimsTransformation actually produces: userId
    // and status, and deliberately no role. There is no role parameter because a deactivated principal has
    // no role claim — that absence is the fail-closed half of the block, so a test that supplied one would
    // be asserting against an identity production can never emit (BLUM-6386).
    protected static void AuthenticateAsDeactivated(Guid userId)
    {
        TestIdentityContext.Set(TestIdentity.Deactivated(userId));
    }

    // Authenticate as a real seeded user and let production's ApplicationUserClaimsTransformation derive
    // every claim from the database, instead of synthesising them here. Slower and it requires the user row
    // to exist, but it is the only way a test covers the transformation and the middleware together —
    // everything else in the suite short-circuits the transformation by never emitting an object id.
    protected static void AuthenticateThroughClaimsTransformation(Guid subId)
    {
        TestIdentityContext.Set(TestIdentity.ForSubject(subId));
    }

    protected static void AuthenticateAsAdmin()
    {
        TestIdentityContext.Reset();
    }

    protected async Task<int> SaveChangesAsync()
    {
        var result = await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        return result;
    }
}

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var testIdentity = TestIdentityContext.Current;

        // Emit the object id alone and stop: production's ApplicationUserClaimsTransformation is registered
        // in the test host too, and it keys off exactly this claim, so it will resolve the profile from the
        // database and add the real claims itself. Every other branch below is the synthesised shortcut,
        // which is why the transformation is otherwise never exercised over HTTP.
        if (testIdentity.SubId is { } subId)
        {
            return Success([new Claim(ClaimConstants.ObjectId, subId.ToString())]);
        }

        // Mirror ApplyClaims exactly for a deactivated user: userId and status, never a role and never an
        // organisation. Synthesising a role here would hide any behaviour that depends on its absence.
        if (string.Equals(testIdentity.Status, Status.Deactivated, StringComparison.OrdinalIgnoreCase))
        {
            return Success(
            [
                new Claim("userId", testIdentity.UserId.ToString()),
                new Claim("status", Status.Deactivated),
            ]);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, testIdentity.Role),
            new("userId", testIdentity.UserId.ToString()),

            // Mirror SetUserClaims once more: an active user's principal always carries an email, and
            // MfaController.Setup refuses without one. Deliberately absent from the deactivated branch
            // above, because production withholds it there too.
            new(ClaimTypes.Email, testIdentity.EmailOrDefault),
        };

        if (testIdentity.Status is { } status)
        {
            claims.Add(new Claim("status", status));
        }

        if (testIdentity.OrganisationId is { } organisationId)
        {
            claims.Add(new Claim("organisationId", organisationId.ToString()));

            // Mirror production's ApplicationUserClaimsTransformation: surface the user's organisation
            // onboarding status so OrganisationSuspendedMiddleware can enforce the suspension block.
            var organisationStatus = await GetOrganisationStatusAsync(organisationId);
            if (organisationStatus is not null)
            {
                claims.Add(new Claim("organisationStatus", organisationStatus));
            }
        }

        return Success(claims);
    }

    private static AuthenticateResult Success(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, "TestScheme"));
    }

    private async Task<string?> GetOrganisationStatusAsync(Guid organisationId)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organisation = await dbContext.Organisations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id.Equals(new OrganisationId(organisationId)));

        return organisation?.OnboardingStatus.ToString();
    }
}