using Application.Submissions;
using Domain.Activities;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

public class FundingSpendValidatorTests
{
    private static readonly ActivityId Activity = ActivityId.New();

    [Fact]
    public void ValidateActuals_AsDraft_WithMissingAmounts_HasNoErrors()
    {
        var errors = FundingSpendValidator.ValidateActuals(
            [Activity],
            new Dictionary<Guid, decimal?>(),
            otherAmount: null,
            enforceRequired: false);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateActuals_OnComplete_WithMissingAmounts_FlagsEachField()
    {
        var errors = FundingSpendValidator.ValidateActuals(
            [Activity],
            new Dictionary<Guid, decimal?>(),
            otherAmount: null,
            enforceRequired: true);

        errors.Should().Contain(e => e.QuestionCode == $"actual-{Activity.Value}");
        errors.Should().Contain(e => e.QuestionCode == "other-spend");
    }

    [Fact]
    public void ValidateActuals_WithNegativeAmount_FlagsEvenWhenNotEnforcingRequired()
    {
        var errors = FundingSpendValidator.ValidateActuals(
            [Activity],
            new Dictionary<Guid, decimal?> { [Activity.Value] = -1m },
            otherAmount: 0m,
            enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].QuestionCode.Should().Be($"actual-{Activity.Value}");
    }

    [Fact]
    public void ValidateActuals_WithAmountOverMax_FlagsTheField()
    {
        var errors = FundingSpendValidator.ValidateActuals(
            [Activity],
            new Dictionary<Guid, decimal?> { [Activity.Value] = 1_000_000_001m },
            otherAmount: 0m,
            enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].QuestionCode.Should().Be($"actual-{Activity.Value}");
    }

    [Fact]
    public void ValidateActuals_WithZeroAmounts_HasNoErrors()
    {
        var errors = FundingSpendValidator.ValidateActuals(
            [Activity],
            new Dictionary<Guid, decimal?> { [Activity.Value] = 0m },
            otherAmount: 0m,
            enforceRequired: true);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateForecast_OnComplete_WithMissingQuarters_FlagsEachActivityQuarterAndOther()
    {
        var errors = FundingSpendValidator.ValidateForecast(
            [Activity],
            [2, 3, 4],
            new Dictionary<(Guid, int), decimal?>(),
            new Dictionary<int, decimal?>(),
            enforceRequired: true);

        errors.Should().Contain(e => e.QuestionCode == $"forecast-{Activity.Value}-q2");
        errors.Should().Contain(e => e.QuestionCode == $"forecast-{Activity.Value}-q4");
        errors.Should().Contain(e => e.QuestionCode == "other-forecast-q3");
    }

    [Fact]
    public void ValidateForecast_WithNoRemainingQuarters_HasNoErrorsEvenOnComplete()
    {
        // A Q4 report has nothing left to forecast — completing the section must not require any forecast amount.
        var errors = FundingSpendValidator.ValidateForecast(
            [Activity],
            [],
            new Dictionary<(Guid, int), decimal?>(),
            new Dictionary<int, decimal?>(),
            enforceRequired: true);

        errors.Should().BeEmpty();
    }
}