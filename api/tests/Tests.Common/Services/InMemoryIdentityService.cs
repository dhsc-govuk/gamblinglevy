using System.Security.Claims;
using Application.Common.Interfaces;
using Domain.Locations;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;

namespace Tests.Common.Services;

/// <summary>
/// Stands in for the real IdentityService, which reads the request principal. This one answers from the
/// principal too whenever there is a request — TestAuthHandler puts the same values on it that the ambient
/// identity holds, so nothing changes for the existing suite — and falls back to the ambient identity only
/// on the Sender path, where there is no HttpContext to read.
///
/// It used to answer from the ambient identity unconditionally, which meant it could not see a claim that
/// production withholds. That is what let BLUM-6386 ship a /mfa/logout exemption that answered 401 in
/// production while passing in CI: the real service found no userId claim on a deactivated principal, and
/// this one handed back the ambient user id regardless.
///
/// The ambient fallback is not universal: an identity that exists only as a subject id has no answer to give
/// off the request path, and says so rather than guessing — see FromClaims.
/// </summary>
public class InMemoryIdentityService(IHttpContextAccessor httpContextAccessor) : IIdentityService
{
    public Option<UserId> GetUserId()
    {
        return FromClaims(
            principal =>
            {
                var userId = principal.FindFirstValue("userId");
                return string.IsNullOrEmpty(userId) ? Option<UserId>.None : new UserId(new Guid(userId));
            },
            () => new UserId(TestIdentityContext.Current.UserId));
    }

    // The one accessor deliberately not routed through FromClaims below. The subject id is the single value
    // TestIdentity.ForSubject genuinely carries, so there is nothing to resolve and nothing to get wrong on
    // the Sender path — unlike the role and user id, which that identity leaves as placeholders.
    public Option<SubId> GetSubId()
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal is { Identity.IsAuthenticated: true })
        {
            var claimed = principal.FindFirstValue(ClaimConstants.ObjectId);
            return string.IsNullOrEmpty(claimed) ? Option<SubId>.None : new SubId(new Guid(claimed));
        }

        var ambient = TestIdentityContext.Current.SubId;
        return ambient.HasValue ? new SubId(ambient.Value) : Option<SubId>.None;
    }

    public Option<UserRole> GetRole()
    {
        return FromClaims(
            principal =>
            {
                var role = principal.FindFirstValue(ClaimTypes.Role);
                return string.IsNullOrEmpty(role) ? Option<UserRole>.None : UserRole.From(role);
            },
            () => UserRole.From(TestIdentityContext.Current.Role));
    }

    public Option<OrganisationId> GetOrganisationId()
    {
        return FromClaims(
            principal =>
            {
                var organisationId = principal.FindFirstValue("organisationId");
                return string.IsNullOrEmpty(organisationId)
                    ? Option<OrganisationId>.None
                    : new OrganisationId(new Guid(organisationId));
            },
            () =>
            {
                var organisationId = TestIdentityContext.Current.OrganisationId;
                return organisationId.HasValue
                    ? new OrganisationId(organisationId.Value)
                    : Option<OrganisationId>.None;
            });
    }

    public Option<LocationId> GetLocationId()
    {
        return Option<LocationId>.None;
    }

    public Option<string> GetEmail()
    {
        // Answered from the principal like the rest, rather than from a constant. It used to hand back the
        // main admin's address whoever was signed in, which made three questions unaskable: whether a
        // deactivated principal has an email at all (production withholds it, so MfaController.Setup would
        // refuse where the fake accepted), whether the claims transformation emits one, and whether an
        // MFA enrolment as an LA user labels the authenticator with that user's address or the admin's.
        return FromClaims(
            principal =>
            {
                var email = principal.FindFirstValue(ClaimTypes.Email);
                return string.IsNullOrEmpty(email) ? Option<string>.None : email;
            },
            () => TestIdentityContext.Current.EmailOrDefault);
    }

    private Option<T> FromClaims<T>(Func<ClaimsPrincipal, Option<T>> fromPrincipal, Func<Option<T>> fromAmbient)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal is { Identity.IsAuthenticated: true })
        {
            return fromPrincipal(principal);
        }

        // TestIdentity.ForSubject carries a subject id and placeholders — Role.Admin and Guid.Empty — that
        // exist only to satisfy the record. Over HTTP they are never read, because production's claims
        // transformation resolves the real ones from the database. There is no transformation on the Sender
        // path and no principal to read, so falling back to the ambient values would silently run the test as
        // an empty-guid platform admin: the opposite of what the helper was asked for, and it would pass.
        if (TestIdentityContext.Current.SubId is not null)
        {
            throw new InvalidOperationException(
                "AuthenticateThroughClaimsTransformation resolves claims from the database during "
                + "authentication, so it only describes an identity for requests sent through Client. This "
                + "call has no HttpContext — drive the assertion over HTTP, or use AuthenticateAs / "
                + "AuthenticateAsAdmin for the Sender path.");
        }

        return fromAmbient();
    }
}