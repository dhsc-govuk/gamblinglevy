using Api.Common;
using Application.Common.Features;
using Application.Common.Settings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.Tests.Integration.Features;

/// <summary>
/// That a flag set the way ops set one — an App Service / DAC environment variable over the shipped
/// appsettings.json — actually reaches <see cref="IFeatureFlags"/>, through the same
/// <c>configuration.Get&lt;ApplicationSettings&gt;()</c> call SetupModule makes. The suite's own host cannot show this:
/// its settings binding happens before appsettings.Test.json arrives, which is why the other tests flip the
/// singleton instead.
///
/// No class fixture, so no PostgreSQL container. Each test reads environment variables under its own prefix, so
/// setting one cannot leak into a parallel class.
/// </summary>
public class FeatureFlagConfigurationTests
{
    [Fact]
    public void ShouldShipEveryFlagSwitchedOff_InAppSettings()
    {
        // appsettings.json is the list ops and DAC read to learn which flags exist; a flag missing from it is
        // one nobody knows to switch on.
        var settings = Bind(Configuration());

        settings.Features.Keys.Should().BeEquivalentTo(Enum.GetNames<Feature>());
        settings.Features.Values.Should().OnlyContain(enabled => !enabled, "new work ships dark to production");
        new FeatureFlags(settings).Enabled.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSwitchAFlagOn_FromAnEnvironmentVariableOverTheShippedDefault()
    {
        const string prefix = "FEATURE_FLAG_TEST_ON_";
        using var variable = new EnvironmentVariable(prefix + "Features__BLUM_7637", "true");

        var settings = Bind(Configuration(prefix));

        new FeatureFlags(settings).IsEnabled(Feature.BLUM_7637).Should().BeTrue();
        FeatureFlags.UnknownKeys(settings).Should().BeEmpty();
    }

    [Fact]
    public void ShouldSwitchAFlagOn_WhenTheEnvironmentVariableIsCasedDifferently()
    {
        const string prefix = "FEATURE_FLAG_TEST_CASE_";
        using var variable = new EnvironmentVariable(prefix + "FEATURES__blum_7637", "true");

        var settings = Bind(Configuration(prefix));

        new FeatureFlags(settings).IsEnabled(Feature.BLUM_7637).Should().BeTrue();
    }

    [Fact]
    public void ShouldReportAMistypedEnvironmentVariable_AndLeaveTheFeatureOff()
    {
        const string prefix = "FEATURE_FLAG_TEST_TYPO_";
        using var variable = new EnvironmentVariable(prefix + "Features__BLUM_763", "true");

        var settings = Bind(Configuration(prefix));

        new FeatureFlags(settings).IsEnabled(Feature.BLUM_7637).Should().BeFalse();
        FeatureFlags.UnknownKeys(settings).Should().Equal("BLUM_763");
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void ShouldLeaveTheFlagOffAndReportIt_WhenTheValueIsNotTrueOrFalse(string value)
    {
        // The binder does not fail on a value that is not a bool — it silently drops the entry, so the key never
        // reaches ApplicationSettings and the unknown-key check cannot see it either. Off is the safe outcome; the
        // raw-section check is what makes it visible in the boot log.
        const string prefix = "FEATURE_FLAG_TEST_BAD_";
        using var variable = new EnvironmentVariable(prefix + "Features__BLUM_7637", value);
        var configuration = Configuration(prefix);

        var settings = Bind(configuration);

        new FeatureFlags(settings).IsEnabled(Feature.BLUM_7637).Should().BeFalse();
        FeatureFlagConfiguration.InvalidValues(configuration).Should().ContainSingle()
            .Which.Key.Should().Be("BLUM_7637");
    }

    [Fact]
    public void ShouldReportNoInvalidValues_ForTheShippedAppSettings()
    {
        FeatureFlagConfiguration.InvalidValues(Configuration()).Should().BeEmpty();
    }

    private static IConfiguration Configuration(string? environmentPrefix = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);
        if (environmentPrefix is not null)
        {
            builder.AddEnvironmentVariables(environmentPrefix);
        }

        return builder.Build();
    }

    private static ApplicationSettings Bind(IConfiguration configuration) =>
        configuration.Get<ApplicationSettings>() ?? throw new InvalidOperationException("appsettings.json bound to nothing");

    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;

        public EnvironmentVariable(string name, string value)
        {
            _name = name;
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, null);
    }
}