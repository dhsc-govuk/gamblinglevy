using Application.Common.ReportSnapshots;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

// BLUM-8329: strategic-feedback.csv as one row per organisation. A snapshot frozen since then carries each question's
// code, which decides its column. One frozen before names each question only by the label it had at submit time, so
// every label a question was frozen under must still find its column — including the BLUM-6613 wording that the Figma
// alignment replaced and BLUM_7637's " (optional)" suffix.
public class StrategicFeedbackCsvTests
{
    // Rows frozen before codes existed: label and answer only.
    private static ReportSnapshotDocument Feedback(params (string Question, string Answer)[] rows) =>
        FeedbackRows(rows.Select(r => new ReportSnapshotRow(r.Question, r.Answer)).ToArray());

    private static ReportSnapshotDocument FeedbackRows(params ReportSnapshotRow[] rows) =>
        new([], null, [new ReportSnapshotSection("Strategic feedback", rows)]);

    // The code is what a copy edit cannot move, so a row carrying one is placed by it whatever its label now says.
    [Fact]
    public void ShouldPlaceARowByItsCode_WhateverItsLabel()
    {
        var snapshot = FeedbackRows(
            new ReportSnapshotRow("Some future wording of the learnings question?", "Yes", "SF_LEARNINGS"),
            new ReportSnapshotRow("Tell us about your challenges", "Challenges", "SF_CHALLENGES"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal(string.Empty, "Challenges", string.Empty, "Yes");
    }

    // A coded row that is not one of the four is a question with no column — a label that happens to match one of the
    // legacy wordings must not pull it into someone else's.
    [Fact]
    public void ShouldLeaveOutACodedRowThatIsNotOneOfTheFourQuestions_EvenWhenItsLabelMatchesOne()
    {
        var snapshot = FeedbackRows(new ReportSnapshotRow("What challenges have you encountered?", "Answer", "SF_SOMETHING_NEW"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public void ShouldPutEachAnswerUnderItsQuestion_WhenLabelledWithTheCurrentCopy()
    {
        var snapshot = Feedback(
            ("What other gambling harms prevention work have you focused on this quarter?", "Prevention"),
            ("What challenges have you encountered?", "Challenges"),
            ("Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?", "Feedback"),
            ("Do you have any learnings you’d like to share with other local authorities?", "Yes"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal("Prevention", "Challenges", "Feedback", "Yes");
    }

    [Fact]
    public void ShouldIgnoreTheOptionalSuffix()
    {
        var snapshot = Feedback(
            ("What other gambling harms prevention work have you focused on this quarter? (optional)", "Prevention"),
            ("What challenges have you encountered? (optional)", "Challenges"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal("Prevention", "Challenges", string.Empty, string.Empty);
    }

    [Fact]
    public void ShouldPlaceAnswersFrozenUnderTheOriginalWording()
    {
        var snapshot = Feedback(
            ("What other prevention work have you focused on this quarter?", "Prevention"),
            ("What challenges have you encountered?", "Challenges"),
            ("How could the Department of Health and Social Care (DHSC) better support your work?", "Feedback"),
            ("What learnings would you like to share with other local authorities?", "Learnings"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal("Prevention", "Challenges", "Feedback", "Learnings");
    }

    [Fact]
    public void ShouldTreatATypedApostropheAsTheTypographicOne()
    {
        var snapshot = Feedback(("Do you have any learnings you'd like to share with other local authorities?", "No"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal(string.Empty, string.Empty, string.Empty, "No");
    }

    // Position is not what decides the column: the rows arrive out of order and one is unrecognised, and nothing slides.
    [Fact]
    public void ShouldLeaveAnUnrecognisedQuestionOut_WithoutShiftingTheOthers()
    {
        var snapshot = Feedback(
            ("Do you have any learnings you’d like to share with other local authorities?", "Yes"),
            ("How is delivery progressing?", "On track"),
            ("What challenges have you encountered?", "Challenges"));

        ReportSnapshotCsv.StrategicFeedbackAnswers(snapshot).Should().Equal(string.Empty, "Challenges", string.Empty, "Yes");
    }

    [Fact]
    public void ShouldReturnNull_WhenTheSnapshotHasNoStrategicFeedback()
    {
        ReportSnapshotCsv.StrategicFeedbackAnswers(new ReportSnapshotDocument([], null, [])).Should().BeNull();
    }
}