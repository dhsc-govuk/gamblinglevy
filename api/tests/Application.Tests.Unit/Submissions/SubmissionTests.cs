using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

public class SubmissionTests
{
    private const string KeyResultsModule = "activity_key_results";
    private const string FundingModule = "funding_spent";

    private static readonly UserId Actor = UserId.New();

    private static Submission Build() =>
        Submission.Start(
            SubmissionId.New(),
            CollectionId.New(),
            OrganisationId.New(),
            [
                (FormModuleId.New(), KeyResultsModule),
                (FormModuleId.New(), FundingModule),
            ],
            Actor);

    private static SubmissionSection Section(Submission submission, string moduleCode) =>
        submission.Sections.Single(s => s.ModuleCode == moduleCode);

    [Fact]
    public void Start_SnapshotsOneNotStartedSectionPerAssignedModule()
    {
        var submission = Build();

        submission.AreActivitiesConfirmed.Should().BeFalse();
        submission.ActivitiesReviewRequired.Should().BeFalse();
        submission.Sections.Should().HaveCount(2);
        submission.Sections.Select(s => s.ModuleCode).Should().BeEquivalentTo(KeyResultsModule, FundingModule);
        submission.Sections.Should().OnlyContain(s => s.Status.IsNotStarted && !s.ReviewRequired);
    }

    [Fact]
    public void ConfirmActivities_MarksConfirmedAndClearsEveryReviewFlag()
    {
        var submission = Build();
        submission.ConfirmActivities(Actor);
        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.InProgress, Actor);
        submission.FlagActivityListChanged();

        submission.ConfirmActivities(Actor);

        submission.AreActivitiesConfirmed.Should().BeTrue();
        submission.ActivitiesReviewRequired.Should().BeFalse();
        submission.Sections.Should().OnlyContain(s => !s.ReviewRequired);
    }

    [Fact]
    public void FlagActivityListChanged_BeforeConfirmation_IsANoOp()
    {
        var submission = Build();
        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.InProgress, Actor);

        submission.FlagActivityListChanged();

        submission.ActivitiesReviewRequired.Should().BeFalse();
        submission.Sections.Should().OnlyContain(s => !s.ReviewRequired);
    }

    [Fact]
    public void FlagActivityListChanged_AfterConfirmation_FlagsOnlyStartedSections()
    {
        var submission = Build();
        submission.ConfirmActivities(Actor);
        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.InProgress, Actor);

        submission.FlagActivityListChanged();

        submission.ActivitiesReviewRequired.Should().BeTrue();
        Section(submission, KeyResultsModule).ReviewRequired.Should().BeTrue();
        Section(submission, FundingModule).ReviewRequired.Should().BeFalse();
    }

    [Fact]
    public void SetSectionStatus_Completed_ClearsSectionFlagAndSubmissionFlagWhenNoneRemain()
    {
        var submission = Build();
        submission.ConfirmActivities(Actor);
        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.InProgress, Actor);
        submission.FlagActivityListChanged();

        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.Completed, Actor);

        Section(submission, KeyResultsModule).ReviewRequired.Should().BeFalse();
        submission.ActivitiesReviewRequired.Should().BeFalse();
    }

    [Fact]
    public void SetSectionStatus_Completed_KeepsSubmissionFlagWhileAnotherSectionIsFlagged()
    {
        var submission = Build();
        submission.ConfirmActivities(Actor);
        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.InProgress, Actor);
        submission.SetSectionStatus(FundingModule, SubmissionSectionStatus.InProgress, Actor);
        submission.FlagActivityListChanged();

        submission.SetSectionStatus(KeyResultsModule, SubmissionSectionStatus.Completed, Actor);

        Section(submission, KeyResultsModule).ReviewRequired.Should().BeFalse();
        Section(submission, FundingModule).ReviewRequired.Should().BeTrue();
        submission.ActivitiesReviewRequired.Should().BeTrue();
    }

    [Fact]
    public void SetSectionStatus_UnknownModuleCode_Throws()
    {
        var submission = Build();

        var act = () => submission.SetSectionStatus("unknown_module", SubmissionSectionStatus.InProgress, Actor);

        act.Should().Throw<SubmissionSectionNotFoundException>();
    }
}