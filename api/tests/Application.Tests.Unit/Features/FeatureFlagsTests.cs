using Application.Common.Features;
using Application.Common.Settings;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Features;

/// <summary>
/// The per-environment flag lookup. A flag switched on by mistake ships unreleased work to production, and a
/// flag that silently reads as off hides a release — both come down to how a configuration key is matched.
/// </summary>
public class FeatureFlagsTests
{
    private const Feature AnyFeature = Feature.BLUM_7637;

    [Fact]
    public void ShouldBeOff_WhenTheFlagIsNotConfigured()
    {
        var flags = new FeatureFlags(new ApplicationSettings());

        flags.IsEnabled(AnyFeature).Should().BeFalse();
        flags.Enabled.Should().BeEmpty();
    }

    [Fact]
    public void ShouldBeOff_WhenTheFlagIsConfiguredFalse()
    {
        var flags = new FeatureFlags(SettingsWith((AnyFeature.ToString(), false)));

        flags.IsEnabled(AnyFeature).Should().BeFalse();
    }

    [Fact]
    public void ShouldBeOn_WhenTheFlagIsConfiguredTrue()
    {
        var flags = new FeatureFlags(SettingsWith((AnyFeature.ToString(), true)));

        flags.IsEnabled(AnyFeature).Should().BeTrue();
        flags.Enabled.Should().Equal(AnyFeature);
    }

    [Fact]
    public void ShouldMatchTheConfiguredNameRegardlessOfCase()
    {
        // Environment variables are typed by hand into the Azure portal and DAC's own config; casing is not the
        // mistake worth failing a release over. A plain dictionary, as the binder may hand back, is case-sensitive.
        var settings = new ApplicationSettings
        {
            Features = new Dictionary<string, bool> { [AnyFeature.ToString().ToLowerInvariant()] = true },
        };

        new FeatureFlags(settings).IsEnabled(AnyFeature).Should().BeTrue();
        FeatureFlags.UnknownKeys(settings).Should().BeEmpty();
    }

    [Fact]
    public void ShouldNotTreatANumericKeyAsAFeature()
    {
        // Enum.TryParse would read "0" as the first member, so a stray Features__0=true would switch it on.
        var settings = SettingsWith(("0", true));

        new FeatureFlags(settings).Enabled.Should().BeEmpty();
        FeatureFlags.UnknownKeys(settings).Should().Equal("0");
    }

    [Fact]
    public void ShouldReportAMistypedKeyAsUnknownAndLeaveTheFeatureOff()
    {
        var settings = SettingsWith(("BLUM_763", true));

        new FeatureFlags(settings).IsEnabled(AnyFeature).Should().BeFalse();
        FeatureFlags.UnknownKeys(settings).Should().Equal("BLUM_763");
    }

    [Fact]
    public void ShouldSeeAChangeMadeAfterConstruction()
    {
        // The integration suite flips a flag on the shared settings singleton after the host has built this
        // service; a snapshot taken at construction would make that flip invisible.
        var settings = new ApplicationSettings();
        var flags = new FeatureFlags(settings);

        settings.Features[AnyFeature.ToString()] = true;

        flags.IsEnabled(AnyFeature).Should().BeTrue();
    }

    private static ApplicationSettings SettingsWith(params (string Key, bool Value)[] entries)
    {
        var settings = new ApplicationSettings();
        foreach (var (key, value) in entries)
        {
            settings.Features[key] = value;
        }

        return settings;
    }
}