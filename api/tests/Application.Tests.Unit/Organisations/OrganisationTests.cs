using Domain.Organisations;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Organisations;

// BLUM-7051 makes ons_code required, but only for Local Authorities: an ONS code identifies an LA geography,
// so a VCSE organisation genuinely has none. The rule therefore lives in the validators, which can see the
// organisation's type, and NOT in the domain factory, which is shared by both.
//
// These are tripwires against re-tightening the aggregate. A Guard.NotNullOrEmptyOrWhiteSpace on onsCode here
// (or a non-nullable parameter) would make VCSE organisations impossible to create through any path, and
// because ons_code is uniquely indexed the placeholder workaround only ever works once — the platform would
// be limited to a single VCSE organisation. Change the validators, not this.
public class OrganisationTests
{
    private static Organisation Build(string? onsCode, OrgType type) =>
        Organisation.New(
            OrganisationId.New(),
            "Test Organisation",
            onsCode,
            Region.London,
            type,
            "E1000001",
            OnboardingStatus.Invited);

    [Fact]
    public void New_KeepsTheOnsCode_WhenSupplied()
    {
        Build("E06000001", OrgType.LocalAuthority).OnsCode.Should().Be("E06000001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void New_AllowsAVcseOrganisationWithNoOnsCode(string? onsCode)
    {
        var act = () => Build(onsCode, OrgType.Vcse);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateDetails_AllowsClearingTheOnsCode()
    {
        var organisation = Build("E06000001", OrgType.Vcse);

        organisation.UpdateDetails("Test Organisation", Region.London, OrgType.Vcse, "E1000001", null);

        organisation.OnsCode.Should().BeNull();
    }

    // The E-code goes the other way (BLUM-7050): it is required for both organisation types, e_code is NOT NULL
    // in the database, and the rows that predate the rule were backfilled with the "TBC" sentinel by the same
    // migration — so unlike onsCode there is no legacy state this guard can lock out, and the aggregate is the
    // right place to refuse a missing one. The validators still hold their own NotEmpty rule so the API answers
    // 400 with usable copy rather than surfacing this exception.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void New_RejectsAnOrganisationWithNoECode(string? eCode)
    {
        var act = () => Organisation.New(
            OrganisationId.New(),
            "No E-code Organisation",
            "E06000002",
            Region.London,
            OrgType.LocalAuthority,
            eCode!,
            OnboardingStatus.Invited);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_RejectsClearingTheECode(string? eCode)
    {
        var organisation = Build("E06000001", OrgType.LocalAuthority);

        var act = () => organisation.UpdateDetails("Test Organisation", Region.London, OrgType.LocalAuthority, eCode!, "E06000001");

        act.Should().Throw<ArgumentException>();
        organisation.ECode.Should().Be("E1000001");
    }
}