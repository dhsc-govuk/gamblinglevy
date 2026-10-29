using Application.Common.Features;
using Application.Common.Settings;
using FluentAssertions;
using Infrastructure.Persistence.Seed.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Unit.Features;

/// <summary>
/// The boot-time step that moves flagged reference data into the state its flag calls for. There is no record of
/// what ran before, so every run has to hand each convergence the flag's current state — in both directions.
/// </summary>
public class FeatureConvergenceRunnerTests
{
    private const Feature AnyFeature = Feature.BLUM_7637;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldPassEachConvergenceItsFlagsCurrentState(bool enabled)
    {
        var settings = new ApplicationSettings();
        settings.Features[AnyFeature.ToString()] = enabled;
        var convergence = new RecordingConvergence(AnyFeature);

        await Runner(settings, convergence).RunAsync();

        convergence.Calls.Should().Equal(enabled);
    }

    [Fact]
    public async Task ShouldRevertWhenTheFlagIsNotConfiguredAtAll()
    {
        // An environment that has never heard of the flag — production, by default — must converge to "off", not
        // be skipped: that is what reverts a change left behind by an earlier "on" boot.
        var convergence = new RecordingConvergence(AnyFeature);

        await Runner(new ApplicationSettings(), convergence).RunAsync();

        convergence.Calls.Should().Equal(false);
    }

    [Fact]
    public async Task ShouldRefuseTwoConvergencesForOneFeature()
    {
        var first = new RecordingConvergence(AnyFeature);
        var second = new RecordingConvergence(AnyFeature);

        var run = () => Runner(new ApplicationSettings(), first, second).RunAsync();

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{AnyFeature}*");
        first.Calls.Should().BeEmpty("nothing may run once the set of convergences is known to be ambiguous");
        second.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldFailTheRunWhenAConvergenceThrows()
    {
        var run = () => Runner(new ApplicationSettings(), new ThrowingConvergence(AnyFeature)).RunAsync();

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("converge failed");
    }

    private static FeatureConvergenceRunner Runner(ApplicationSettings settings, params IFeatureConvergence[] convergences) =>
        new(convergences, new FeatureFlags(settings), NullLogger<FeatureConvergenceRunner>.Instance);

    private sealed class RecordingConvergence(Feature feature) : IFeatureConvergence
    {
        public List<bool> Calls { get; } = new();

        public Feature Feature { get; } = feature;

        public Task<int> ConvergeAsync(bool enabled, CancellationToken cancellationToken)
        {
            Calls.Add(enabled);
            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingConvergence(Feature feature) : IFeatureConvergence
    {
        public Feature Feature { get; } = feature;

        public Task<int> ConvergeAsync(bool enabled, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("converge failed");
    }
}