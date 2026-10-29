using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Permissions;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence.Extensions;
using Xunit;

namespace Application.Tests.Unit.Persistence;

/// <summary>
/// BLUM-8751 — <see cref="PermissionScopeExtensions.ScopedTo{T}"/> is the one place a role becomes an
/// organisation scope for the repositories. Before it, four copies of the switch each ended in an empty result
/// for a role none of their branches named, which is how a role with no access goes unnoticed. These pin that
/// every supported role has a scope and that anything else fails loudly, naming the role.
/// </summary>
public class PermissionScopeTests
{
    private static readonly OrganisationId OwnOrganisation = OrganisationId.New();
    private static readonly OrganisationId OtherOrganisation = OrganisationId.New();

    private static readonly Row[] Rows =
    [
        new("own-1", OwnOrganisation),
        new("own-2", OwnOrganisation),
        new("other-1", OtherOrganisation),
    ];

    public static TheoryData<string> SupportedRoles() => new(UserRole.SupportedRoleNames);

    // The tripwire: a role added to UserRole.SupportedRoles that is neither the DHSC admin nor an organisation
    // member fails here, before any repository can be asked to scope it.
    [Theory]
    [MemberData(nameof(SupportedRoles))]
    public void EverySupportedRole_HasAScope(string roleName)
    {
        var permission = Permission.OrganisationPermission(UserRole.From(roleName), OwnOrganisation, UserId.New());

        var act = () => Scope(permission).ToList();

        act.Should().NotThrow("every supported role must be either the DHSC admin or an organisation member");
    }

    [Fact]
    public void Admin_SeesEveryOrganisationsRows()
    {
        var rows = Scope(Permission.AdminPermission(UserRole.Admin)).ToList();

        rows.Should().BeEquivalentTo(Rows);
    }

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public void OrganisationMember_SeesOnlyTheirOwnOrganisationsRows(string roleName)
    {
        var permission = Permission.OrganisationPermission(UserRole.From(roleName), OwnOrganisation, UserId.New());

        var rows = Scope(permission).ToList();

        rows.Select(x => x.Name).Should().BeEquivalentTo(["own-1", "own-2"]);
    }

    [Fact]
    public void UnscopedRole_Throws_NamingTheRole()
    {
        var permission = Permission.AdminPermission(UnsupportedRole("auditor"));

        var act = () => Scope(permission);

        act.Should().Throw<PermissionScopeException>().WithMessage("*\"auditor\"*");
    }

    [Fact]
    public void OrganisationMemberWithoutAnOrganisation_Throws()
    {
        // AdminPermission carries no organisation; PermissionsService never builds one for an LA role, but
        // nothing stops code from doing it by hand.
        var permission = Permission.AdminPermission(UserRole.Lead);

        var act = () => Scope(permission);

        act.Should().Throw<PermissionScopeException>().WithMessage($"*\"{Role.Lead}\"*organisation*");
    }

    [Fact]
    public void UnscopedRole_ThrowsWhenTheQueryIsBuilt_NotWhenItRuns()
    {
        var permission = Permission.AdminPermission(UnsupportedRole("auditor"));

        // Not enumerated: an EF query would otherwise fail later, as an error about its source rather than
        // about the role.
        var act = () => Rows.AsQueryable().ScopedTo(permission, InOrganisation);

        act.Should().Throw<PermissionScopeException>();
    }

    private static IQueryable<Row> Scope(Permission permission) => Rows.AsQueryable().ScopedTo(permission, InOrganisation);

    private static Expression<Func<Row, bool>> InOrganisation(OrganisationId organisationId) => x => x.OrganisationId == organisationId;

    // UserRole.From refuses anything outside the supported set, which is the point; reflection is the only way to
    // stand in for a role added there without a scope.
    private static UserRole UnsupportedRole(string value) =>
        (UserRole)Activator.CreateInstance(
            typeof(UserRole),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [value],
            culture: null)!;

    private sealed record Row(string Name, OrganisationId OrganisationId);
}