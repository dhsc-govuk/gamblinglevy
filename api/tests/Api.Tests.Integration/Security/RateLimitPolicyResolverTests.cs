using Api.Common;
using Application.Common.Security;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// BLUM-7239 AC4 — the table mapping an endpoint to the budget it draws on.
///
/// Lives in this project because RateLimitPolicyResolver is in the Api layer, which Application.Tests.Unit
/// does not reference. It declares no class fixture, so it boots no host and creates no database — worth
/// knowing, because every fixture-bearing class in this assembly does both.
///
/// Every template below is copied from a real [Route]/[Http*] pair in api/src/Api/Controllers. That is the
/// point of the class: the resolver is only correct relative to routes that actually exist, so a template
/// invented for the test would prove nothing.
/// </summary>
public class RateLimitPolicyResolverTests
{
    [Theory]

    // Exempt. The first two are Azure's container probes, called on a schedule this service does not
    // control, so counting them would spend a budget on the platform's own health checking.
    [InlineData("GET", "/health", RateLimitPolicy.None)]
    [InlineData("GET", "/", RateLimitPolicy.None)]
    [InlineData("GET", "uptime", RateLimitPolicy.None)]

    // A request that matched no endpoint is counted, not exempt. Exempting it meant a flood aimed at any
    // nonexistent path charged neither the caller's partition nor the Global ceiling, so the whole control
    // was bypassed by appending a character to the URL.
    [InlineData("GET", "unknown", RateLimitPolicy.Read)]
    [InlineData("POST", "unknown", RateLimitPolicy.Write)]

    // Reads.
    [InlineData("GET", "activities", RateLimitPolicy.Read)]
    [InlineData("GET", "activities/{activityId:guid}", RateLimitPolicy.Read)]
    [InlineData("GET", "admin/audit-logs", RateLimitPolicy.Read)]
    [InlineData("GET", "profile/me", RateLimitPolicy.Read)]
    [InlineData("GET", "quarterly-report/{collectionId:guid}/task-list", RateLimitPolicy.Read)]

    // Writes, across all four mutating verbs the API actually uses.
    [InlineData("POST", "activities", RateLimitPolicy.Write)]
    [InlineData("PUT", "activities/{activityId:guid}", RateLimitPolicy.Write)]
    [InlineData("DELETE", "activities/{activityId:guid}", RateLimitPolicy.Write)]
    [InlineData("PATCH", "organisations/local-authorities/{organisationId:guid}/allocation", RateLimitPolicy.Write)]
    [InlineData("POST", "organisations/local-authorities", RateLimitPolicy.Write)]
    [InlineData("POST", "profile/sessions/{sessionId:guid}/revoke", RateLimitPolicy.Write)]

    // The offline report upload (BLUM-8310) is a small CSV sent again after every round of fixes, often by several
    // officers behind one council address, so it is an ordinary write rather than an export.
    [InlineData("POST", "quarterly-report/{collectionId:guid}/offline/upload", RateLimitPolicy.Write)]

    // Expensive. Two of the three exports are GETs, which is the whole reason the cost rules sit above the
    // verb rules — as Reads these would run at three hundred a minute while building a ZIP of per-section
    // CSVs across every organisation in the collection.
    [InlineData("GET", "quarterly-report/{collectionId:guid}/export", RateLimitPolicy.Export)]
    [InlineData("GET", "data-collections/{collectionId:guid}/submissions/export", RateLimitPolicy.Export)]
    [InlineData("GET", "data-collections/{collectionId:guid}/submissions/{organisationId:guid}/export", RateLimitPolicy.Export)]
    [InlineData("GET", "activities/{activityId:guid}/export", RateLimitPolicy.Export)]
    [InlineData("POST", "organisations/local-authorities/bulk-import", RateLimitPolicy.Export)]
    [InlineData("POST", "organisations/local-authorities/bulk-allocations", RateLimitPolicy.Export)]
    [InlineData("POST", "data-collections/{collectionId:guid}/send-reminder", RateLimitPolicy.Export)]

    // The template downloads share the bulk- prefix but are a few hundred bytes of static CSV. Classing them
    // as Export would mean fetching the template twice ate a fifth of the budget needed to then import.
    [InlineData("GET", "organisations/local-authorities/bulk-import/template", RateLimitPolicy.Read)]
    [InlineData("GET", "organisations/local-authorities/bulk-allocations/template", RateLimitPolicy.Read)]

    // The offline report's templates (BLUM-8310) are small reads.
    [InlineData("GET", "quarterly-report/{collectionId:guid}/offline/activity-results/template", RateLimitPolicy.Read)]
    [InlineData("GET", "quarterly-report/{collectionId:guid}/offline/funding-spent/template", RateLimitPolicy.Read)]
    [InlineData("GET", "quarterly-report/{collectionId:guid}/offline/strategic-feedback/template", RateLimitPolicy.Read)]

    // Auth covers the MFA endpoints that take a credential. Not the status read: the SPA issues that on every
    // mount, and putting routine page loads on the tightest per-address budget meant one council's staff,
    // who all share a NAT egress address, could exhaust it between them.
    [InlineData("GET", "mfa/status", RateLimitPolicy.Read)]
    [InlineData("POST", "mfa/verify", RateLimitPolicy.Auth)]
    [InlineData("POST", "mfa/verify/recovery", RateLimitPolicy.Auth)]
    [InlineData("POST", "mfa/setup/verify", RateLimitPolicy.Auth)]
    [InlineData("POST", "mfa/disable", RateLimitPolicy.Auth)]
    [InlineData("POST", "mfa/recovery-codes/regenerate", RateLimitPolicy.Auth)]
    public void ShouldResolveTheExpectedPolicy(string method, string routeTemplate, RateLimitPolicy expected)
    {
        RateLimitPolicyResolver.Resolve(method, routeTemplate).Should().Be(expected);
    }

    [Fact]
    public void ShouldTreatAnUnmatchedRequestAsExempt()
    {
        // A request that matched no endpoint is on its way to a 404. Counting it would let anyone spend a
        // budget by typing nonsense, and there is no template to bucket it by in any case.
        RateLimitPolicyResolver.Resolve("GET", string.Empty).Should().Be(RateLimitPolicy.None);
    }

    [Fact]
    public void ShouldNotCareWhetherTheTemplateHasALeadingSlash()
    {
        // Controller routes arrive without one and minimal-API routes with one, so a rule written for either
        // form alone would silently mis-bucket half the surface.
        RateLimitPolicyResolver.Resolve("GET", "/activities").Should().Be(RateLimitPolicy.Read);
        RateLimitPolicyResolver.Resolve("POST", "/mfa/verify").Should().Be(RateLimitPolicy.Auth);
    }

    [Fact]
    public void ShouldTreatHeadAsARead()
    {
        RateLimitPolicyResolver.Resolve("HEAD", "activities").Should().Be(RateLimitPolicy.Read);
    }

    [Fact]
    public void ShouldExposeEveryRateLimitHeaderItSets()
    {
        // Setting the headers is only half of AC5. None of the four is CORS-safelisted, and the SPA runs on a
        // different origin from the API in every deployed environment, so a header the CORS policy does not
        // name in Access-Control-Expose-Headers is invisible to the browser — while remaining perfectly
        // visible to curl, to the network tab and to this test suite, which has no origin at all.
        //
        // That asymmetry is the whole point of pinning it here: the failure mode is a client that cannot tell
        // the user when to come back, with nothing anywhere reporting a problem.
        RateLimitHeaders.All.Should().BeEquivalentTo(
            new[] { "RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset", "Retry-After", "Content-Disposition" },
            "SetupCors exposes exactly this list, so a header added to the middleware without adding it here would be unreadable by the SPA");
    }
}