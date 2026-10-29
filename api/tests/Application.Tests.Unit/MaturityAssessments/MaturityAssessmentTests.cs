using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

public class MaturityAssessmentTests
{
    private static readonly UserId Actor = UserId.New();

    private static MaturityAssessment Start() =>
        MaturityAssessment.Start(MaturityAssessmentId.New(), CollectionId.New(), OrganisationId.New(), MaturityProtocol.SectionCodes, Actor, DateTime.UtcNow);

    [Fact]
    public void Start_CreatesEveryCatalogueSectionAsNotStarted()
    {
        var assessment = Start();

        assessment.Sections.Select(s => s.SectionCode).Should().Equal(MaturityProtocol.SectionCodes);
        assessment.Sections.Should().OnlyContain(s => s.Status.IsNotStarted);
        assessment.IsSubmitted.Should().BeFalse();
        assessment.AreSectionsCompleted(MaturityProtocol.SectionCodes).Should().BeFalse();
    }

    [Fact]
    public void AreSectionsCompleted_OnlyOnceEverySectionIsCompleted()
    {
        var assessment = Start();

        foreach (var code in MaturityProtocol.SectionCodes.SkipLast(1))
        {
            assessment.SetSectionStatus(code, MaturityAssessmentSectionStatus.Completed, Actor, DateTime.UtcNow);
        }

        assessment.AreSectionsCompleted(MaturityProtocol.SectionCodes).Should().BeFalse();

        assessment.SetSectionStatus(MaturityProtocol.SectionCodes[^1], MaturityAssessmentSectionStatus.Completed, Actor, DateTime.UtcNow);
        assessment.AreSectionsCompleted(MaturityProtocol.SectionCodes).Should().BeTrue();
    }

    // BLUM-8093: the gate is the collection's CURRENT forms. A section the admin has since unticked must not hold the
    // submit back, and one added after Start (no row yet) must.
    [Fact]
    public void AreSectionsCompleted_IgnoresRowsOutsideTheGivenSections()
    {
        var assessment = MaturityAssessment.Start(MaturityAssessmentId.New(), CollectionId.New(), OrganisationId.New(), ["la_capacity", "next_steps"], Actor, DateTime.UtcNow);

        assessment.SetSectionStatus("la_capacity", MaturityAssessmentSectionStatus.Completed, Actor, DateTime.UtcNow);

        assessment.AreSectionsCompleted(["la_capacity"]).Should().BeTrue();
        assessment.AreSectionsCompleted(["la_capacity", "next_steps"]).Should().BeFalse();
    }

    [Fact]
    public void AreSectionsCompleted_TreatsASectionWithNoRowAsIncomplete()
    {
        var assessment = MaturityAssessment.Start(MaturityAssessmentId.New(), CollectionId.New(), OrganisationId.New(), ["la_capacity"], Actor, DateTime.UtcNow);
        assessment.SetSectionStatus("la_capacity", MaturityAssessmentSectionStatus.Completed, Actor, DateTime.UtcNow);

        assessment.AreSectionsCompleted(["la_capacity", "needs_assessment"]).Should().BeFalse();
        assessment.AreSectionsCompleted([]).Should().BeFalse();
    }

    // A form the admin added to the collection after this assessment started has no row yet; its first write creates
    // one (the read side already lists it as not started), rather than a 500 on a section the LA can see.
    [Fact]
    public void SetSectionStatus_CreatesARowForASectionStartedBeforeItExisted()
    {
        var assessment = MaturityAssessment.Start(MaturityAssessmentId.New(), CollectionId.New(), OrganisationId.New(), ["la_capacity"], Actor, DateTime.UtcNow);

        assessment.SetSectionStatus("next_steps", MaturityAssessmentSectionStatus.InProgress, Actor, DateTime.UtcNow);

        assessment.Sections.Should().HaveCount(2);
        assessment.Sections.Single(s => s.SectionCode == "next_steps").Status.Should().Be(MaturityAssessmentSectionStatus.InProgress);
    }

    [Fact]
    public void Submit_FreezesTheReferenceAndSubmitter()
    {
        var assessment = Start();
        foreach (var code in MaturityProtocol.SectionCodes)
        {
            assessment.SetSectionStatus(code, MaturityAssessmentSectionStatus.Completed, Actor, DateTime.UtcNow);
        }

        assessment.Submit("2027-0001", Actor, DateTime.UtcNow);
        assessment.AttachSnapshot("{\"sections\":[]}");

        assessment.IsSubmitted.Should().BeTrue();
        assessment.ReferenceNumber.Should().Be("2027-0001");
        assessment.SubmittedBy.Should().Be(Actor);
        assessment.SubmittedAt.Should().NotBeNull();
        assessment.SnapshotJson.Should().Be("{\"sections\":[]}");
    }

    [Fact]
    public void Record_KeepsTheSourceItWasGiven()
    {
        var answer = MaturityAssessmentAnswer.Record(MaturityAssessmentAnswerId.New(), MaturityAssessmentId.New(), "MA_DESIGNATED_LEAD", "yes", AnswerSource.Quarterly, Actor, DateTime.UtcNow);

        answer.Source.Should().Be(AnswerSource.Quarterly);
        answer.Source.IsUser.Should().BeFalse();
        AnswerSource.User.IsUser.Should().BeTrue();
    }
}