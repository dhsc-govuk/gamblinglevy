using System.Net;
using Application.Common.Features;
using Application.Common.Settings;
using Domain.AuditLogs;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seed;
using Infrastructure.Persistence.Seed.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Seeding;

/// <summary>
/// A seeding step that throws at boot must not take a live service down with it (BLUM-8749). Before this, every
/// seeder rethrew out of Program.cs, so one drifted reference row crash-looped the App Service. The failing step here
/// is an IFeatureConvergence. It is the last step to run, which is also what proves the steps before it still ran,
/// and it is the step whose runner used to call its failure "fatal to boot" on purpose.
///
/// The rest of the suite boots with Seeding.FailStartupOnError on (TestFactory), so a real seeding defect still
/// fails it. This class's own host turns it off, as every deployed environment has it.
/// </summary>
public class SeedingFailureBootTests(FailingSeedWebFactory factory) : IClassFixture<FailingSeedWebFactory>
{
    [Fact]
    public async Task ShouldStartAndServe_WhenASeedingStepFailsAtBoot()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<ConvergenceProbe>().Calls.Should().BeGreaterThan(0, "the failing step must actually have run at boot");

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Categories.AsNoTracking().CountAsync())
            .Should().Be(ActivityTaxonomySeedData.Categories.Count, "the steps before the failing one still seed");
        (await context.KeyResultQuestions.AsNoTracking().AnyAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNameTheFailedStep_WhenATestDataResetCannotReseed()
    {
        // The reset's audit row records the failure without its message, which can carry SQL. Since every seeding
        // failure arrives as one SeedingIncompleteException, the type alone would say nothing, so it names the steps.
        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/test-data/reset");
        request.Headers.Add("X-Data-Reset-Secret", "integration-test-harness-reset-secret");

        var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await context.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == AuditAction.SystemTestDataReset);
        row.After.Should().Contain("\"succeeded\": false").And.Contain("SeedingIncompleteException: FeatureConvergenceRunner");
    }

    [Fact]
    public async Task ShouldStopTheHost_WhenFailStartupOnErrorIsSet()
    {
        var strict = new FailingSeedWebFactory { FailStartupOnError = true };
        await strict.InitializeAsync();
        try
        {
            var start = () => strict.CreateClient();

            // The factory reports the boot failure by its base exception, which is the failed step's own: the
            // SeedingIncompleteException around it is an AggregateException, and those are unwrapped.
            start.Should().Throw<InvalidOperationException>().WithMessage("Simulated seeding failure*");
        }
        finally
        {
            await strict.DisposeAsync();
            await ((IAsyncDisposable)strict).DisposeAsync();
        }
    }
}

/// <summary>The suite's host, with one seeding step that always throws, and the deployed environments' boot setting.</summary>
public sealed class FailingSeedWebFactory : IntegrationTestWebFactory
{
    public bool FailStartupOnError { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ConvergenceProbe>();
            services.AddScoped<IFeatureConvergence, ThrowingConvergence>();

            // Runs after the base factory's own override, which turns this on for the rest of the suite.
            var settings = (ApplicationSettings)services.Single(s => s.ServiceType == typeof(ApplicationSettings)).ImplementationInstance!;
            settings.Seeding.FailStartupOnError = FailStartupOnError;
        });
    }
}

public sealed class ConvergenceProbe
{
    public int Calls { get; set; }
}

internal sealed class ThrowingConvergence(ConvergenceProbe probe) : IFeatureConvergence
{
    public Feature Feature => Feature.BLUM_7637;

    public Task<int> ConvergeAsync(bool enabled, CancellationToken cancellationToken)
    {
        probe.Calls++;
        throw new InvalidOperationException("Simulated seeding failure (SeedingFailureBootTests).");
    }
}