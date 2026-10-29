using Application.Common.Seeding;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Unit.Seed;

/// <summary>
/// Seeding runs on every boot of a live service, so one failing step must not stop the steps after it, and the
/// failure must still surface (BLUM-8749). Boot catches it and starts anyway; the test-data reset and the
/// integration suite let it propagate.
/// </summary>
public class SeedStepRunnerTests
{
    [Fact]
    public async Task ShouldRunEveryStep_WhenAnEarlierOneFails()
    {
        var ran = new List<string>();
        var resets = 0;

        var run = () => SeedStepRunner.RunAsync(
            [Recording("first", ran), Throwing("second"), Recording("third", ran)],
            () => resets++,
            NullLogger.Instance);

        var thrown = await run.Should().ThrowAsync<SeedingIncompleteException>();
        thrown.Which.FailedSteps.Should().Equal("second");
        thrown.Which.InnerExceptions.Should().ContainSingle().Which.Message.Should().Be("second failed");
        ran.Should().Equal("first", "third");
        resets.Should().Be(1, "the failed step's tracked rows must not reach the next step's SaveChangesAsync");
    }

    [Fact]
    public async Task ShouldNameEveryFailedStep()
    {
        var run = () => SeedStepRunner.RunAsync([Throwing("first"), Throwing("second")], () => { }, NullLogger.Instance);

        var thrown = await run.Should().ThrowAsync<SeedingIncompleteException>();
        thrown.Which.FailedSteps.Should().Equal("first", "second");
        thrown.Which.Message.Should().Contain("first, second");
        thrown.Which.InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ShouldNotThrowOrReset_WhenEveryStepSucceeds()
    {
        var ran = new List<string>();
        var resets = 0;

        await SeedStepRunner.RunAsync([Recording("first", ran), Recording("second", ran)], () => resets++, NullLogger.Instance);

        ran.Should().Equal("first", "second");
        resets.Should().Be(0);
    }

    private static SeedStep Recording(string name, List<string> ran) => new(name, () =>
    {
        ran.Add(name);
        return Task.CompletedTask;
    });

    private static SeedStep Throwing(string name) => new(name, () => throw new InvalidOperationException($"{name} failed"));
}