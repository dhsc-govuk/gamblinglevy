using Application.MaturityAssessments.Export;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// The two CSV shapes built from a frozen snapshot: the per-LA long file (one row per visible question) and the
// DHSC wide file (one row per submitter, one column per catalogue question, role pairs for the repeater).
public class MaturityAssessmentCsvTests
{
    private static readonly string Roles = MaturityRolesAnswer.Serialize(
    [
        new MaturityRolesAnswer.Entry("public_health_officer", "81_100", null),
        new MaturityRolesAnswer.Entry("other", "up_to_20", "Data analyst"),
    ]);

    private static readonly MaturityAssessmentSnapshotDocument Snapshot = new(
    [
        new MaturitySnapshotSection(
            "la_capacity",
            "LA capacity",
            [
            new MaturitySnapshotPage(
                "people_and_roles",
                "People and roles",
                [
                new MaturitySnapshotRow("MA_DESIGNATED_LEAD", "Does your public health team currently have designated members responsible for gambling harms prevention?", "Yes", "yes", null),
            ]),
            new MaturitySnapshotPage(
                "funded_roles",
                "Funded roles",
                [
                new MaturitySnapshotRow("MA_FUNDED_ROLES", "Which roles are being funded by the levy?", "Role 1: Public Health Officer (81% to 100%); Role 2: Data analyst (Up to 20%)", Roles, null),
            ]),
        ]),
        new MaturitySnapshotSection(
            "needs_assessment",
            "Needs assessment",
            [
            new MaturitySnapshotPage(
                "engagement",
                "Engagement",
                [
                new MaturitySnapshotRow("MA_NA_ENGAGEMENT__POLICE", "Police", "Yes", "yes", "Who did you engage with when creating your needs assessment?"),
            ]),
        ]),
    ]);

    [Fact]
    public void LongRows_EmitOneRowPerVisibleQuestion_WithTheGroupLabelInTheQuestion()
    {
        var rows = MaturityAssessmentCsv.LongRows(["Main Council"], Snapshot).ToList();

        rows.Should().HaveCount(3);
        rows[0].Should().Equal("Main Council", "LA capacity", "People and roles", "Does your public health team currently have designated members responsible for gambling harms prevention?", "Yes");
        rows[2].Should().Equal("Main Council", "Needs assessment", "Engagement", "Who did you engage with when creating your needs assessment? – Police", "Yes");
    }

    [Fact]
    public void WideRow_FillsEveryCatalogueColumn_LeavingUnansweredOnesBlank()
    {
        var columns = MaturityAssessmentCsv.WideColumns(maxRoles: 2);
        var header = MaturityAssessmentCsv.WideHeader(["Organisation"], columns).ToList();

        var row = MaturityAssessmentCsv.WideRow(["Main Council"], Snapshot, columns);

        row.Should().HaveCount(header.Count);
        row[header.IndexOf("Does your public health team currently have designated members responsible for gambling harms prevention?")].Should().Be("Yes");
        row[header.IndexOf("Who did you engage with when creating your needs assessment? – Police")].Should().Be("Yes");
        row[header.IndexOf("Role 1: role")].Should().Be("Public Health Officer");
        row[header.IndexOf("Role 1: time")].Should().Be("81% to 100%");
        row[header.IndexOf("Role 2: role")].Should().Contain("Data analyst");
        row[header.IndexOf("Role 2: time")].Should().Be("Up to 20%");
        row[header.IndexOf("What are your biggest priorities when it comes to preventing gambling harms in your local authority?")].Should().BeEmpty();
    }

    // BLUM-9112: a "None" answer fills Role 1's role column and leaves its time blank.
    [Fact]
    public void WideRow_WritesNoneInTheFirstRoleColumn_WithNoTime()
    {
        var none = MaturityRolesAnswer.Serialize([new MaturityRolesAnswer.Entry("none", string.Empty, null)]);
        var snapshot = new MaturityAssessmentSnapshotDocument(
        [
            new MaturitySnapshotSection(
                "la_capacity",
                "LA capacity",
                [
                new MaturitySnapshotPage(
                    "funded_roles",
                    "Funded roles",
                    [
                    new MaturitySnapshotRow("MA_FUNDED_ROLES", "Which roles are being funded by the levy?", "None", none, null),
                ]),
            ]),
        ]);
        var columns = MaturityAssessmentCsv.WideColumns(maxRoles: MaturityAssessmentCsv.MaxRoles([snapshot]));
        var header = MaturityAssessmentCsv.WideHeader(["Organisation"], columns).ToList();

        var row = MaturityAssessmentCsv.WideRow(["Main Council"], snapshot, columns);

        row[header.IndexOf("Role 1: role")].Should().Be("None");
        row[header.IndexOf("Role 1: time")].Should().BeEmpty();
    }

    [Fact]
    public void MaxRoles_IsTheWidestRepeaterAcrossSnapshots()
    {
        var empty = new MaturityAssessmentSnapshotDocument([]);

        MaturityAssessmentCsv.MaxRoles([empty]).Should().Be(0);
        MaturityAssessmentCsv.MaxRoles([empty, Snapshot]).Should().Be(2);
    }

    [Fact]
    public void WideColumns_WithNoRoles_HaveNoRoleColumns()
    {
        MaturityAssessmentCsv.WideColumns(maxRoles: 0).Should().OnlyContain(c => c.RoleIndex == null);
    }

    // BLUM-8093: a window's wide file has columns for the forms the admin kept, not a block of always-blank ones for
    // the sections they unticked.
    [Fact]
    public void WideColumns_ForASubsetOfSections_CoverOnlyThoseSectionsQuestions()
    {
        string[] sections = ["la_capacity", "next_steps"];
        var expected = MaturityQuestionResolver.ResolveAll()
            .Where(q => sections.Contains(q.Section))
            .Select(q => q.Code)
            .ToHashSet();

        var columns = MaturityAssessmentCsv.WideColumns(maxRoles: 2, sections);

        columns.Select(c => c.Code).Distinct().Should().BeEquivalentTo(expected);
    }
}