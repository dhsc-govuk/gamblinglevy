using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// The maturity sections' collection-form ids (BLUM-7943) are persisted in collection_forms, so they are data, not
// code: a changed literal orphans every collection holding the old one. These pin the values and keep the migration
// that back-filled existing collections in step with the catalogue.
public class MaturityCollectionFormsTests
{
    [Fact]
    public void All_FollowsTheCatalogueSectionsInOrder()
    {
        MaturityCollectionForms.All.Select(f => f.SectionCode).Should().Equal(MaturityProtocol.SectionCodes);
        MaturityCollectionForms.All.Select(f => f.Title).Should().Equal(MaturityProtocol.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Ids_AreDistinctAndNeverEmpty()
    {
        MaturityCollectionForms.Ids.Should().OnlyHaveUniqueItems();
        MaturityCollectionForms.Ids.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public void Ids_AreThePersistedLiterals()
    {
        MaturityCollectionForms.All.Select(f => (f.SectionCode, f.Id.ToString())).Should().Equal(
            ("la_capacity", "aa721c9a-7034-4444-b706-f61167e7022e"),
            ("needs_assessment", "a963f8c5-a06b-4a71-8f4e-33635f4f557d"),
            ("strategic_approach", "14e6c7ed-3fa0-42fa-80b0-9a5f19c36ea9"),
            ("awareness_training_signposting", "882b90b0-0cc8-4417-b56b-111928e2bbe5"),
            ("planning_licensing", "6f9b7386-fc4f-446c-a77d-b1a9e84904ea"),
            ("local_campaigns", "88cd0c58-026f-49e0-a6a8-b978b04562ab"),
            ("advertising_restrictions", "079504a1-7b31-4505-bf70-ca3a6ced0bbe"),
            ("working_with_vcse", "6ab2174b-9321-42fe-9cd7-4d2b47f7ac92"),
            ("other_preventative_services", "37ac8124-0133-4728-85cb-c364462eda0b"),
            ("next_steps", "ecbf4e5b-78fa-4158-a73f-3bf166c497d2"));
    }

    [Fact]
    public void FindAndIsMaturityFormId_RecogniseOnlyCatalogueIds()
    {
        var first = MaturityCollectionForms.All[0];

        MaturityCollectionForms.Find(first.Id).Should().Be(first);
        MaturityCollectionForms.IsMaturityFormId(first.Id).Should().BeTrue();
        MaturityCollectionForms.Find(Guid.NewGuid()).Should().BeNull();
        MaturityCollectionForms.IsMaturityFormId(Guid.NewGuid()).Should().BeFalse();
    }

    // BLUM-8093: the LA's sections are the collection's forms, always in catalogue order whatever order the admin's
    // selection was stored in, and never a quarterly module id that somehow rode along.
    [Fact]
    public void SectionCodesFor_ReturnsTheCollectionsMaturityFormsInCatalogueOrder()
    {
        var collection = Collection.New(
            CollectionId.New(),
            "Maturity subset",
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
        var nextSteps = MaturityCollectionForms.All[^1];
        var laCapacity = MaturityCollectionForms.All[0];
        collection.SetForms([FormModuleId.From(nextSteps.Id), FormModuleId.From(Guid.NewGuid()), FormModuleId.From(laCapacity.Id)]);

        MaturityCollectionForms.SectionCodesFor(collection).Should().Equal(laCapacity.SectionCode, nextSteps.SectionCode);
        MaturityCollectionForms.Includes(collection, nextSteps.SectionCode).Should().BeTrue();
        MaturityCollectionForms.Includes(collection, MaturityCollectionForms.All[1].SectionCode).Should().BeFalse();
    }

    [Fact]
    public void BackfillMigration_WritesEverySectionId()
    {
        var sql = string.Join(
            Environment.NewLine,
            new BLUM_7943_MaturityCollectionForms().UpOperations.OfType<SqlOperation>().Select(o => o.Sql));

        sql.Should().Contain("c.type = 'maturity_assessment'");
        foreach (var id in MaturityCollectionForms.Ids)
        {
            sql.Should().Contain($"'{id}'::uuid");
        }
    }
}