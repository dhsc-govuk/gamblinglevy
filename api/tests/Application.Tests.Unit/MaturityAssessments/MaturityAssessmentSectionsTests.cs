using Application.MaturityAssessments;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// BLUM-8093: an open assessment follows the collection's current forms; a submitted one is frozen to what it was
// submitted with, whatever the admin has ticked since.
public class MaturityAssessmentSectionsTests
{
    private static readonly DateTime SubmittedAt = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnOpenAssessment_FollowsTheCollectionsCurrentForms()
    {
        var collection = CollectionWithForms("next_steps", "la_capacity");

        MaturityAssessmentSections.For(collection, null, null, ["needs_assessment"])
            .Should().Equal("la_capacity", "next_steps");
    }

    [Fact]
    public void ASubmittedAssessment_KeepsItsSnapshotsSections()
    {
        var collection = CollectionWithForms("needs_assessment");
        var snapshot = MaturityAssessmentSnapshotJson.Serialize(new MaturityAssessmentSnapshotDocument(
        [
            new MaturitySnapshotSection("la_capacity", "LA capacity", []),
            new MaturitySnapshotSection("next_steps", "Next steps for your LA", []),
        ]));

        MaturityAssessmentSections.For(collection, SubmittedAt, snapshot, ["la_capacity", "next_steps", "strategic_approach"])
            .Should().Equal("la_capacity", "next_steps");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"sections\":[]}")]
    [InlineData("not json")]
    public void ASubmittedAssessmentWithNoUsableSnapshot_FallsBackToItsSectionRows_InCatalogueOrder(string? snapshotJson)
    {
        var collection = CollectionWithForms("needs_assessment");

        MaturityAssessmentSections.For(collection, SubmittedAt, snapshotJson, ["next_steps", "la_capacity"])
            .Should().Equal("la_capacity", "next_steps");
    }

    private static Collection CollectionWithForms(params string[] sectionCodes)
    {
        var collection = Collection.New(
            CollectionId.New(),
            "Maturity sections fixture",
            null,
            CollectionType.MaturityAssessment,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            UserId.New(),
            saveAsDraft: true,
            utcNow: DateTime.UtcNow);
        collection.SetForms(sectionCodes
            .Select(code => FormModuleId.From(MaturityCollectionForms.All.Single(f => f.SectionCode == code).Id))
            .ToList());
        return collection;
    }
}