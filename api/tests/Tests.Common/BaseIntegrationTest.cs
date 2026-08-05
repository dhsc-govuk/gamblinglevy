using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Organisations;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    protected static void AuthenticateAs(string role, Guid userId, Guid organisationId)
    {
        TestIdentityContext.Set(new TestIdentity(role, userId, organisationId));
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
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, testIdentity.Role),
            new("userId", testIdentity.UserId.ToString()),
        };

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

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return AuthenticateResult.Success(ticket);
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