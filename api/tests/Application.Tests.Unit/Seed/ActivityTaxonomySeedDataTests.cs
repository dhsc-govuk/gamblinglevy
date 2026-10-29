using Domain.Categories;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Xunit;

namespace Application.Tests.Unit.Seed;

// The taxonomy catalogue is matched by name on every boot (BLUM-8749): a duplicate name would collapse two rows into
// one key, and a name the key-result protocol resolves by that is missing here would leave its questions unseeded.
public class ActivityTaxonomySeedDataTests
{
    [Fact]
    public void CategoryNames_AreUnique()
    {
        ActivityTaxonomySeedData.Categories.Select(c => c.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TypeNames_AreUniqueWithinEachCategory()
    {
        foreach (var category in ActivityTaxonomySeedData.Categories)
        {
            category.TaskNames.Should().OnlyHaveUniqueItems(category.Name);
            category.TaskNames.Should().NotBeEmpty(category.Name);
        }
    }

    [Fact]
    public void Categories_IncludeEveryNameBehaviourKeysOn()
    {
        ActivityTaxonomySeedData.Categories.Select(c => c.Name).Should().Contain(
        [
            CategoryNames.PeopleAndRoles,
            CategoryNames.JointPartnership,
            CategoryNames.EducationCampaignsAndOutreach,
            CategoryNames.HelpAdviceAndAccessToServices,
        ]);
    }

    [Fact]
    public void AnOtherType_IsAlwaysLastInItsCategory()
    {
        // The wizard lists types in SequenceOrder, and the catch-all "Other ..." type belongs at the end.
        foreach (var category in ActivityTaxonomySeedData.Categories)
        {
            var others = category.TaskNames.Where(n => n.StartsWith("Other", StringComparison.Ordinal)).ToList();
            others.Should().HaveCountLessThanOrEqualTo(1, category.Name);
            if (others.Count == 1)
            {
                category.TaskNames[^1].Should().Be(others[0], category.Name);
            }
        }

        Category(CategoryNames.EducationCampaignsAndOutreach).TaskNames[^1].Should().Be(ActivityTaxonomySeedData.OtherEducationTaskName);
        Category(CategoryNames.HelpAdviceAndAccessToServices).TaskNames[^1].Should().Be(ActivityTaxonomySeedData.OtherHelpTaskName);
    }

    [Fact]
    public void JointPartnership_HasOneTypeThatIsNotAnOtherType()
    {
        // The client keys its free-text "name the other type" reveal off the "Other" prefix.
        Category(CategoryNames.JointPartnership).TaskNames.Should().Equal(ActivityTaxonomySeedData.JointPartnershipTaskName);
        ActivityTaxonomySeedData.JointPartnershipTaskName.Should().NotStartWith("Other");
    }

    [Fact]
    public void EveryKeyResultProtocolBlock_NamesATypeInTheTaxonomy()
    {
        var typeNames = ActivityTaxonomySeedData.Categories.SelectMany(c => c.TaskNames).ToHashSet();

        KeyResultProtocolSeedData.Protocol.Select(p => p.TaskName).Should().OnlyContain(name => typeNames.Contains(name));
    }

    private static ActivityTaxonomySeedData.CategorySeed Category(string name) =>
        ActivityTaxonomySeedData.Categories.Single(c => c.Name == name);
}