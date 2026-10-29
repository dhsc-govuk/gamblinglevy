using Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.ValueObjects;

public class UserRoleTests
{
    // The MVP role set (BLUM-7038 AC6). This is a deliberate tripwire, not a restatement of the code: the
    // action-level [Authorize] overrides that used to keep view-only roles out of the mutating endpoints
    // were removed once they became identical to their controllers' class-level lists, so re-introducing a
    // role that must NOT mutate means re-introducing those attributes. Fail here first and go read
    // Role.AnyOrganisationMember, Role.UserManagers, AssignableRoles and every [Authorize] before widening.
    [Fact]
    public void SupportedRoles_AreExactlyTheThreeMvpRoles()
    {
        UserRole.SupportedRoleNames.Should().BeEquivalentTo(
            [Role.Admin, Role.OrganisationAdmin, Role.Lead],
            "adding or removing a role changes who can reach every [Authorize(Roles = ...)] endpoint; "
            + "re-examine Role.AnyOrganisationMember, Role.UserManagers, AssignableRoles, "
            + "PermissionScopeExtensions.ScopedTo, LastActiveAdminGuard and UserProfileRepository before changing this");
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public void IsSupported_IsTrue_ForEveryMvpRole(string role)
    {
        UserRole.IsSupported(role).Should().BeTrue();
    }

    [Theory]
    [InlineData("location admin")]
    [InlineData("contributor")]
    [InlineData("finance")]
    [InlineData("read only")]
    [InlineData("Admin")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_IsFalse_ForRemovedUnknownOrMiscasedRoles(string? role)
    {
        UserRole.IsSupported(role).Should().BeFalse();
    }

    [Theory]
    [InlineData("location admin")]
    [InlineData("contributor")]
    [InlineData("finance")]
    [InlineData("read only")]
    public void From_Throws_ForRolesRemovedByBlum7038(string role)
    {
        // users.role is materialised through From(), so a row still holding one of these throws on read.
        // That is precisely why the BLUM_7038_RemoveNonMvpRoles migration must collapse them to 'lead'.
        var act = () => UserRole.From(role);

        act.Should().Throw<UnsupportedUserRoleException>();
    }

    [Fact]
    public void IsOrganisationMember_IsTrue_ForLaRolesOnly()
    {
        UserRole.OrganisationAdmin.IsOrganisationMember().Should().BeTrue();
        UserRole.Lead.IsOrganisationMember().Should().BeTrue();
        UserRole.Admin.IsOrganisationMember().Should().BeFalse("DHSC admins are not scoped to an LA");
    }

    [Fact]
    public void IsOrganisationMember_AgreesWithTheAuthorizeAttributeString()
    {
        // The predicate is derived from Role.AnyOrganisationMember precisely so the two cannot drift —
        // Lead sat in the attribute lists while UserProfileRepository's hand-written role list omitted it,
        // which is what left LA Leads authorised but claimless (BLUM-7038).
        var attributeRoles = Role.AnyOrganisationMember.Split(',');

        attributeRoles.Should().OnlyContain(role => UserRole.From(role).IsOrganisationMember());
    }
}