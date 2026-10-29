using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// BLUM-6101 — the guard that makes "access control is enforced at the API layer on every endpoint" a thing that
/// fails rather than a thing a reviewer has to remember.
///
/// Every other authorisation test in this suite drives a request at an endpoint somebody thought to write a test
/// for. That proves the endpoints we know about behave, and nothing at all about the next controller action, which
/// is the one that will be missing its role list. This class walks the real
/// <see cref="EndpointDataSource"/> the application builds, so a new action either satisfies the rules below or
/// names itself in the failure message.
///
/// Modelled on <c>RateLimitCoverageTests</c>, which does the same job for the rate-limit policy table, and sharing
/// its factory: see <see cref="EndpointCoverageCollection"/> for why.
/// </summary>
[Collection(EndpointCoverageCollection.Name)]
public class AuthorizationCoverageTests(IntegrationTestWebFactory factory)
{
    /// <summary>
    /// Endpoints allowed to be reachable without authenticating. Two are Azure's container probes — the root
    /// probe's template is "/", which trimming the leading slash reduces to the empty string. The other four
    /// share the same gate: ApplicationSettings.DataReset's Enabled flag plus its shared-secret header check,
    /// rather than [Authorize]. internal/test-data/reset is a test-harness-only database reset that has to run
    /// before any user session exists; internal/mfa-data/clear (BLUM-7848) is the same shape of one-time cutover
    /// helper, wiping user_mfas/mfa_sessions so every account re-enrols after switching UseBlobKeyPersistence on;
    /// internal/user-emails/normalise (BLUM-7869) is the one-off backfill correcting a User's email casing on
    /// both the local row and its Entra account — meant to run against Production, unlike its siblings;
    /// internal/user-statistics is a read-only headline-counts view (total/active/logged-in/MFA-pending users)
    /// for whoever holds the shared secret, most useful right after running internal/mfa-data/clear. All four
    /// fail closed in an environment that never configures the section (see DataResetSettings).
    /// </summary>
    private static readonly string[] AnonymousEndpoints =
    [
        string.Empty,
        "health",
        "internal/test-data/reset",
        "internal/mfa-data/clear",
        "internal/user-emails/normalise",
        "internal/user-statistics",
    ];

    /// <summary>
    /// Route families deliberately authorised for any signed-in user rather than for a named role.
    /// <c>mfa</c> is the second factor itself — it must be reachable by all three roles, and by a user who has not
    /// finished enrolling. <c>profile</c> is how the client learns which role it has, so it cannot require one.
    /// Everything else must name its audience: a bare <c>[Authorize]</c> on a business endpoint means every
    /// authenticated user in the service can reach it, including an LA Lead on a DHSC screen.
    /// </summary>
    private static readonly string[] RoleAgnosticFamilies = ["mfa", "profile"];

    /// <summary>
    /// The two submission surfaces and the audience each is built for (BLUM-6101 AC1–AC3). Pinned by route family
    /// rather than per action, because the failure this guards against is a new action on an existing controller
    /// carrying its own <c>[Authorize]</c> that changes the class-level one.
    /// </summary>
    private static readonly (string Family, string Roles)[] SubmissionSurfaces =
    [
        ("quarterly-report", Role.AnyOrganisationMember),
        ("maturity-assessment", Role.AnyOrganisationMember),
        ("data-collections", Role.Admin),
    ];

    private readonly EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

    [Fact]
    public void EveryEndpointShouldRequireAuthentication()
    {
        var open = this.RoutedEndpoints()
            .Where(route => route.IsOpenToAnonymousCallers)
            .Where(route => !AnonymousEndpoints.Contains(route.Template, StringComparer.OrdinalIgnoreCase))
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        open.Should().BeEmpty("an endpoint with no [Authorize], or with [AllowAnonymous], is served to anonymous callers");
    }

    [Fact]
    public void EveryEndpointShouldNameTheRolesItServes()
    {
        var unscoped = this.RoutedEndpoints()
            .Where(route => !route.IsOpenToAnonymousCallers)
            .Where(route => route.Roles.Count == 0)
            .Where(route => !RoleAgnosticFamilies.Any(family => InRouteFamily(route.Template, family)))
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        unscoped.Should().BeEmpty(
            "a bare [Authorize] admits every signed-in user; add the Role.* constant for the audience, or add the "
            + "route to RoleAgnosticFamilies with a reason");
    }

    [Fact]
    public void NoEndpointShouldRequireAContradictoryPairOfRoles()
    {
        // Falls out of resolving stacked attributes as an intersection (see RoutedEndpoints). Two [Authorize]
        // attributes naming disjoint role sets produce an endpoint that every caller is refused from — no compile
        // error, no startup error, and a 403 that looks exactly like a legitimate refusal. Reported separately from
        // the assertion above so the message says "unreachable" rather than "unscoped".
        var unreachable = this.RoutedEndpoints()
            .Where(route => route.NamesRolesSomewhere && route.Roles.Count == 0)
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        unreachable.Should().BeEmpty("stacked [Authorize] attributes must overlap, or no caller can ever reach the endpoint");
    }

    [Fact]
    public void EveryRoleNamedInAnAuthorizeAttributeShouldExist()
    {
        // UserRole.From throws on a value it does not recognise, so a role that only exists in an [Authorize]
        // string is not a compile error and not a startup error — it is an endpoint no one can ever reach, found
        // when a user reports being locked out. The retired roles (contributor, finance, location admin, read only)
        // are the realistic way this happens: they are still legal strings in audit-log history.
        var unknown = this.RoutedEndpoints()
            .SelectMany(route => route.DeclaredRoles.Select(role => (route, role)))
            .Where(pair => !UserRole.IsSupported(pair.role))
            .Select(pair => $"{pair.route.Method} {pair.route.Template} names \"{pair.role}\"")
            .Distinct()
            .ToList();

        unknown.Should().BeEmpty("only the three roles in Domain/ValueObjects/UserRole.cs exist");
    }

    [Fact]
    public void TheSubmissionSurfacesShouldKeepTheirAudiences()
    {
        var wrong = new List<string>();

        foreach (var (family, expected) in SubmissionSurfaces)
        {
            var expectedRoles = SplitRoles(expected);

            wrong.AddRange(this.RoutedEndpoints()
                .Where(route => InRouteFamily(route.Template, family))
                .Where(route => route.IsOpenToAnonymousCallers || !route.Roles.SetEquals(expectedRoles))
                .Select(route => route.IsOpenToAnonymousCallers
                    ? $"{route.Method} {route.Template} is open to anonymous callers, expected [{expected}]"
                    : $"{route.Method} {route.Template} serves [{string.Join(", ", route.Roles.Order(StringComparer.Ordinal))}], expected [{expected}]"));
        }

        wrong.Should().BeEmpty(
            "the LA report surface is for organisation members and the DHSC collections surface is for DHSC; an "
            + "endpoint that changes either is how another organisation's submission becomes readable");
    }

    [Fact]
    public void ShouldHaveFoundTheRealEndpointTable()
    {
        // Without this every assertion above passes vacuously if endpoint discovery ever returns nothing — which is
        // exactly how a guard test stops guarding without anyone noticing (the RateLimitCoverageTests precedent).
        this.RoutedEndpoints().Should().HaveCountGreaterThan(50);
        this.RoutedEndpoints()
            .Count(route => InRouteFamily(route.Template, "quarterly-report"))
            .Should().BeGreaterThan(15, "the LA report surface is the one this ticket is about");
    }

    /// <summary>
    /// Whether a route template belongs to a route family, matched on segment boundaries rather than characters.
    /// A bare <c>StartsWith("profile")</c> also matches "profiles/{id}" and "profile-admin/…", so a future
    /// controller could inherit the role-agnostic exemption purely by sharing a prefix — and the one assertion
    /// whose whole job is to fail on an unscoped [Authorize] would skip it.
    /// </summary>
    private static bool InRouteFamily(string template, string family) =>
        template.Equals(family, StringComparison.OrdinalIgnoreCase)
        || template.StartsWith($"{family}/", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> SplitRoles(string? roles) =>
        (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private List<RoutedEndpoint> RoutedEndpoints()
    {
        return this.endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                // Controller templates arrive without a leading slash and minimal-API ones with it, so normalise
                // exactly as RateLimitCoverageTests does or every family comparison below silently matches nothing.
                var template = (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

                // [AllowAnonymous] does NOT remove the class-level [Authorize] from the metadata — AuthorizationMiddleware
                // short-circuits on this marker instead. Reading only IAuthorizeData would therefore report an
                // anonymous action on an [Authorize]d controller as fully gated, with its inherited role list intact.
                var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

                // Stacked [Authorize] attributes are ANDed, not ORed: each contributes its own requirement to one
                // policy, so the caller must satisfy every one of them and the effective audience is the
                // INTERSECTION of the role lists. Unioning them (the first version of this file) reports a
                // narrowing action-level attribute as a widening one — OrganisationUsersController stacks
                // Role.UserManagers on a class-level list containing Lead, and the union claims Lead may create
                // users. An attribute with no Roles constrains authentication only, so it is not part of the
                // intersection.
                var declaredRoles = authorization.Select(data => SplitRoles(data.Roles)).Where(set => set.Count > 0).ToList();
                var roles = declaredRoles.Count == 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : declaredRoles.Aggregate(
                        new HashSet<string>(declaredRoles[0], StringComparer.Ordinal),
                        (effective, next) =>
                        {
                            effective.IntersectWith(next);
                            return effective;
                        });

                return (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                    .Select(method => new RoutedEndpoint(
                        method,
                        template,
                        allowsAnonymous || authorization.Count == 0,
                        declaredRoles.Count > 0,
                        roles,
                        declaredRoles.SelectMany(set => set).ToHashSet(StringComparer.Ordinal)));
            })

            // Deliberately NOT deduplicated by (method, template). RateLimitCoverageTests can dedupe because its
            // tuple carries nothing but the key; this record carries the metadata being asserted on, so dropping a
            // duplicate would drop its role list — and two endpoints sharing a method and template is exactly the
            // case where one of them is the unprotected one. A duplicate here only repeats a failure message.
            .ToList();
    }

    private sealed record RoutedEndpoint(
        string Method,
        string Template,
        bool IsOpenToAnonymousCallers,
        bool NamesRolesSomewhere,
        HashSet<string> Roles,
        HashSet<string> DeclaredRoles);
}