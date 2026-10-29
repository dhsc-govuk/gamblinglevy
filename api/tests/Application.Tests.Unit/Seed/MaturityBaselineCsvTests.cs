using FluentAssertions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Xunit;

namespace Application.Tests.Unit.Seed;

// The embedded 2025 survey and the mapping over it, checked together: every one of the 152 rows must map to
// catalogue answers, and nothing the mapping produces may be something the catalogue then refuses (which is what
// proves the option-value transcription is exact rather than silently lossy).
public class MaturityBaselineCsvTests
{
    private static readonly IReadOnlyList<MaturityBaselineRow> Rows = MaturityBaselineCsv.LoadEmbedded();

    [Fact]
    public void Resource_HoldsEveryLocalAuthorityRow()
    {
        Rows.Should().HaveCount(152);
        Rows.Should().OnlyContain(r => r.LocalAuthority.Length > 0 && r.Region.Length > 0 && r.Id.Length > 0);
        Rows.Select(r => LocalAuthorityNameMatcher.Normalise(r.LocalAuthority)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryRow_MapsToAnswersTheCatalogueAccepts()
    {
        foreach (var row in Rows)
        {
            var raw = MaturityBaselineMapping.MapRaw(row);
            var conformed = MaturityBaselineMapping.Map(row);

            conformed.Should().BeEquivalentTo(raw, "row {0} ({1}) must not lose an answer to conformance", row.RowNumber, row.LocalAuthority);
            conformed.Count.Should().BeGreaterThan(20, "row {0} ({1})", row.RowNumber, row.LocalAuthority);
        }
    }

    [Fact]
    public void EveryMappedColumn_ExistsInTheResource()
    {
        // The mapping reads columns by name; a header that was renamed in a re-export would silently map nothing.
        var headers = Rows[0].Answers.Keys.ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "designated_gambling_harm_lead", "wte_public_health_officer", "wte_consultant_public_health", "other_roles_wte",
            "business_plan_includes_gambling_harm_fy2526", "part_of_strategic_network", "consider_pooling_resources", "hna_published",
            "hna_link", "hna_data_prevalence", "hna_latest_published", "hna_next_due", "stakeholder_icb_representative",
            "want_regional_hna", "steering_group_status", "steering_icb_rep", "action_plan_status", "strategy_suicide",
            "presented_to_health_and_wellbeing_board", "last_presented_to_hwb", "workplace_policies_for_staff", "training_needed",
            "training_commissioned_primary_care", "screening_tool_primary_care", "treatment_support_access_process",
            "policy_statement_link", "ph_involved_policy_development", "policy_includes_local_area_profile",
            "premises_restricted_refused_due_ph", "ph_invited_licensing_representations", "harms_in_planning_guidance",
            "ph_invited_planning_representations", "awareness_campaign_past_year", "campaign_aims", "campaign_resources_link",
            "ad_restrictions_gambling_council_spaces", "ad_restrictions_introduced_date", "ad_restrictions_type",
            "ad_restrictions_apply_all_council_property", "ad_restrictions_other_commodities", "commissioning_vcse_for_prevention",
            "commissioned_vcse_names", "commissioned_vcse_services", "engage_noncommissioned_vcse", "engaged_vcse_names",
            "engaged_vcse_services", "other_prevention_services", "other_prevention_services_details", "priorities_preventing_gambling_harm",
        };

        headers.Should().Contain(expected);
        headers.Should().NotContain(["respondent_name", "respondent_email", "other_emails"]);
    }

    [Fact]
    public void Parser_HandlesQuotedDelimitersQuotesAndLineBreaks()
    {
        const string csv = "id,region,local_authority,note\n1,London,\"Smith, Jones\",\"line one\nline \"\"two\"\"\"\n2,North West,Plain,\n";

        var rows = MaturityBaselineCsv.Parse(csv);

        rows.Should().HaveCount(2);
        rows[0].LocalAuthority.Should().Be("Smith, Jones");
        rows[0].Answers["note"].Should().Be("line one\nline \"two\"");
        rows[1].Answers["note"].Should().BeEmpty();
    }
}