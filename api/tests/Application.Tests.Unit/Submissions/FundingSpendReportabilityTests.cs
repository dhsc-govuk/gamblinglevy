using Application.Submissions;
using Domain.Activities;
using Domain.Categories;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

// Which "Record funding spent" screen an activity belongs on (BLUM-7672). Actual spend is for work that has started;
// forecast spend is that plus Planned work. The two predicates differ by Planned alone — anything else that keeps an
// activity off one screen keeps it off both.
public class FundingSpendReportabilityTests
{
    private static readonly UserId Actor = UserId.New();

    private static Activity BuildActivity(ActivityStatus status) =>
        Activity.NewComplete(
            ActivityId.New(),
            OrganisationId.New(),
            Actor,
            CategoryId.New(),
            ActivityTaskId.New(),
            status,
            title: "Test activity",
            utcNow: DateTime.UtcNow);

    [Theory]
    [InlineData(ActivityStatuses.Planned, false, true)]
    [InlineData(ActivityStatuses.Active, true, true)]
    [InlineData(ActivityStatuses.Closed, true, true)]
    [InlineData(ActivityStatuses.Finished, true, true)]
    public void PlannedActivities_TakeForecastSpendButNotActualSpend(string status, bool actual, bool forecast)
    {
        var activity = BuildActivity(ActivityStatus.From(status));

        FundingSpendContextResolver.IsActualSpendReportable(activity).Should().Be(actual);
        FundingSpendContextResolver.IsForecastSpendReportable(activity).Should().Be(forecast);
    }

    [Fact]
    public void ADraftActivity_IsOnNeitherScreen()
    {
        var draft = Activity.NewDraft(ActivityId.New(), OrganisationId.New(), Actor, status: ActivityStatus.Planned, utcNow: DateTime.UtcNow);

        FundingSpendContextResolver.IsActualSpendReportable(draft).Should().BeFalse();
        FundingSpendContextResolver.IsForecastSpendReportable(draft).Should().BeFalse();
    }

    [Fact]
    public void ADraftActivityWithNoStatus_IsOnNeitherScreen()
    {
        var draft = Activity.NewDraft(ActivityId.New(), OrganisationId.New(), Actor, DateTime.UtcNow);

        FundingSpendContextResolver.IsActualSpendReportable(draft).Should().BeFalse();
        FundingSpendContextResolver.IsForecastSpendReportable(draft).Should().BeFalse();
    }

    [Theory]
    [InlineData(ActivityStatuses.Planned)]
    [InlineData(ActivityStatuses.Active)]
    public void ADeletedActivity_IsOnNeitherScreen(string status)
    {
        var activity = BuildActivity(ActivityStatus.From(status));
        activity.SoftDelete(Actor, DateTime.UtcNow);

        FundingSpendContextResolver.IsActualSpendReportable(activity).Should().BeFalse();
        FundingSpendContextResolver.IsForecastSpendReportable(activity).Should().BeFalse();
    }
}