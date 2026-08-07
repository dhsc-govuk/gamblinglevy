using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Common;
using Application.Common.Settings;
using Domain.AuditLogs;
using FluentAssertions;
using Infrastructure.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// BLUM-7239 / CWE-770 — per-address request limiting.
///
/// The Nanorisk NR-4099 pen test found no 429, no Retry-After and no lockout anywhere in the service. These
/// cases are the evidence for AC4, AC5 and AC6, kept together because each is a property of the request
/// boundary rather than of any aggregate.
///
/// The limiter is switched off for the whole suite in TestFactory.OverrideApplicationSettings — its counters
/// live in a cache that outlives a test method, and the suite drives essentially every endpoint through one
/// client from one apparent address, so left on it would start answering 429 partway through unrelated
/// classes. This class turns it back on for its own host: IntegrationTestWebFactory is an IClassFixture, so
/// that settings instance belongs to this class alone and cannot leak into a parallel one.
///
/// Note that the endpoints below are exercised with deliberately invalid bodies and made-up ids. That is not
/// laziness — the limiter sits ahead of model binding, authentication and the handler, so what a request
/// would eventually have answered is irrelevant to whether it was counted, and one case asserts exactly that.
/// </summary>
public class RateLimitingTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string ReadRoute = "admin/audit-logs";
    private const string WriteRoute = "organisations/local-authorities";
    private const string HealthRoute = "/health";

    private readonly ApplicationSettings settings;
    private readonly MemoryCacheRateLimiter limiter;
    private readonly CorsOptions corsOptions;

    public RateLimitingTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        this.settings = factory.Services.GetRequiredService<ApplicationSettings>();
        this.limiter = factory.Services.GetRequiredService<MemoryCacheRateLimiter>();
        this.corsOptions = factory.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
    }

    public async Task InitializeAsync()
    {
        // Counters outlive a test method — one host serves the whole class — so a stale budget would make
        // the result of an assertion depend on which tests ran before it.
        this.limiter.Clear();

        this.settings.RateLimiting.Enabled = true;
        this.settings.RateLimiting.Read = new RateLimitPolicySettings(5, 60);
        this.settings.RateLimiting.Write = new RateLimitPolicySettings(3, 60);
        this.settings.RateLimiting.Export = new RateLimitPolicySettings(2, 60);

        // Unlimited unless a case says otherwise, so the process-wide ceiling never masks a partition
        // assertion. ShouldRejectWithTheGlobalPolicy turns it on for itself.
        this.settings.RateLimiting.Global = new RateLimitPolicySettings(0, 60);

        // Unlimited so the audit assertions measure the first-rejection-per-window dedupe rather than the
        // flood caps stacked on top of it — the caps have their own coverage in AuditFloodThrottleTests.
        this.settings.RateLimiting.AuditRateLimitEvents = true;
        this.settings.RateLimiting.MaxAuditRowsPerActorPerHour = 0;
        this.settings.RateLimiting.MaxAuditRowsPerHour = 0;

        await this.ClearAuditLogsAsync();
    }

    public async Task DisposeAsync()
    {
        // Back to the suite-wide default, or every class scheduled after this one on the same host would
        // start counting.
        this.settings.RateLimiting.Enabled = false;
        this.limiter.Clear();

        await this.ClearAuditLogsAsync();
        AuthenticateAsAdmin();
    }

    // ---- AC4: the limit itself ----------------------------------------------------------------------

    [Fact]
    public async Task ShouldAllowRequestsUpToTheLimit_AndRejectTheNextOne()
    {
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await this.GetAsync(ReadRoute)).StatusCode);
        }

        statuses.Take(5).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ShouldCountRequestsThatWereGoingToFailAnyway()
    {
        // The limiter runs before model binding and before the handler, so a request is counted for having
        // been made. If it were counted only on success, a caller could exhaust the service with malformed
        // requests for free — which is the cheapest possible flood to send.
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await this.PostAsync(WriteRoute, "{}")).StatusCode);
        }

        statuses.Take(3).Should().OnlyContain(status => status == HttpStatusCode.BadRequest);
        statuses[3].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ShouldKeepPoliciesIndependent()
    {
        // Exhausting the write budget must not refuse reads: they are separate budgets sized for very
        // different costs.
        for (var i = 0; i < 4; i++)
        {
            await this.PostAsync(WriteRoute, "{}");
        }

        (await this.PostAsync(WriteRoute, "{}")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await this.GetAsync(ReadRoute)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ShouldBucketByRouteTemplate_NotByRequestPath()
    {
        // The lesson InputRejectionThrottle records. A path carries route values, so bucketing on it would
        // give every fresh GUID its own budget — the caller slips past the limit entirely and grows an
        // unbounded cache while doing it.
        this.settings.RateLimiting.Read = new RateLimitPolicySettings(3, 60);

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await this.GetAsync($"{ReadRoute}/{Guid.NewGuid()}")).StatusCode);
        }

        statuses[3].Should().Be(
            HttpStatusCode.TooManyRequests,
            "four different ids matched one route template, so they share one budget");
    }

    [Fact]
    public async Task ShouldNotLimitTheHealthEndpoint()
    {
        // Azure calls this on a schedule the service does not control. A budget spent on the platform's own
        // health checking would take the instance out of rotation.
        for (var i = 0; i < 20; i++)
        {
            (await this.GetAsync(HealthRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await this.GetAsync(HealthRoute);

        response.Headers.Contains("RateLimit-Limit").Should().BeFalse("an uncounted endpoint advertises no budget");
    }

    // ---- AC4: partitioning by caller ----------------------------------------------------------------

    [Fact]
    public async Task ShouldGiveDifferentForwardedAddressesSeparateBudgets()
    {
        // Correct behaviour, and at the same time the executable statement of this feature's largest caveat:
        // the address is read from X-Forwarded-For, nothing yet proves the request arrived through Front
        // Door, so a caller who varies it gets a fresh budget every time. IpAddressNormaliser cannot help —
        // these are genuinely different addresses, not spellings of one. The infrastructure fix (restrict
        // the App Service to the AzureFrontDoor.Backend service tag plus a matching X-Azure-FDID) is tracked
        // as a follow-up; the in-app answer is the Global ceiling, covered below.
        for (var i = 0; i < 6; i++)
        {
            await this.GetAsync(ReadRoute, ip: "203.0.113.1");
        }

        (await this.GetAsync(ReadRoute, ip: "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await this.GetAsync(ReadRoute, ip: "203.0.113.2")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData(" 203.0.113.7 ")]
    [InlineData("203.0.113.7, 10.0.0.1")]
    public async Task ShouldCollapseSpellingsOfOneAddressOntoOneBudget(string spelling)
    {
        // What IpAddressNormaliser is actually in the key path for. Without it, one caller writing their
        // address four ways would hold four budgets — and each spelling would also be a distinct cache entry.
        for (var i = 0; i < 5; i++)
        {
            await this.GetAsync(ReadRoute, ip: "203.0.113.7");
        }

        (await this.GetAsync(ReadRoute, ip: spelling)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ShouldRejectWithTheGlobalPolicy_WhenTheProcessCeilingIsReached()
    {
        // The only in-app mitigation for a caller who varies the address they claim: one unpartitioned
        // counter, which needs a single entry however many addresses arrive.
        this.settings.RateLimiting.Read = new RateLimitPolicySettings(1000, 60);
        this.settings.RateLimiting.Global = new RateLimitPolicySettings(3, 60);

        for (var i = 0; i < 3; i++)
        {
            await this.GetAsync(ReadRoute, ip: $"203.0.113.{i + 10}");
        }

        var response = await this.GetAsync(ReadRoute, ip: "203.0.113.99");

        response.StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests,
            "a fresh address has plenty of partition budget but the process ceiling is spent");

        // Told when to come back, not how much capacity the service has. The ceiling is not this caller's
        // budget, and publishing it would state exactly how much traffic keeps everybody else out.
        response.Headers.Contains("Retry-After").Should().BeTrue();
        response.Headers.Contains("RateLimit-Limit").Should().BeFalse();

        var rows = await this.RateLimitAuditRowsAsync();
        rows.Should().NotBeEmpty();
        rows.Select(Payload).Should().Contain(payload => payload["policy"]!.GetValue<string>() == "Global");
    }

    // ---- AC5: the headers ---------------------------------------------------------------------------

    [Fact]
    public async Task ShouldIncludeRateLimitHeadersOnASuccessfulResponse()
    {
        // The case that decided the whole mechanism. Microsoft.AspNetCore.RateLimiting exposes OnRejected
        // and nothing else — no hook on a successful request and no access to the PartitionedRateLimiter it
        // builds internally — so it can attach these to a 429 and to nothing else. That is the opposite of
        // what the headers are for: they exist so a conforming client slows down BEFORE it is refused.
        //
        // If anyone ever "simplifies" this feature to AddRateLimiter, this is the test that will stop them.
        var first = await this.GetAsync(ReadRoute);

        first.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        HeaderInt(first, "RateLimit-Limit").Should().Be(5);
        HeaderInt(first, "RateLimit-Remaining").Should().Be(4);
        HeaderInt(first, "RateLimit-Reset").Should().BeInRange(1, 60);

        var second = await this.GetAsync(ReadRoute);

        HeaderInt(second, "RateLimit-Remaining").Should().Be(3, "the budget visibly counts down");
    }

    [Fact]
    public async Task ShouldSetRetryAfterAndZeroRemaining_OnTheRejection()
    {
        for (var i = 0; i < 5; i++)
        {
            await this.GetAsync(ReadRoute);
        }

        var response = await this.GetAsync(ReadRoute);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        HeaderInt(response, "RateLimit-Remaining").Should().Be(0);

        var retryAfter = HeaderInt(response, "Retry-After");
        retryAfter.Should().NotBeNull();
        retryAfter.Should().BeGreaterThan(0, "a Retry-After of zero puts a conforming client into a tight retry loop");
        retryAfter.Should().Be(HeaderInt(response, "RateLimit-Reset"), "both describe the same window closing");
    }

    [Fact]
    public async Task ShouldEmitNoHeaders_ForAPolicyConfiguredUnlimited()
    {
        // Advertising "RateLimit-Limit: 0" would tell a conforming client it may never call again.
        this.settings.RateLimiting.Read = new RateLimitPolicySettings(0, 60);

        var response = await this.GetAsync(ReadRoute);

        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        response.Headers.Contains("RateLimit-Limit").Should().BeFalse();
    }

    [Fact]
    public void ShouldExposeItsHeadersThroughCors()
    {
        // Setting the headers is only half of AC5. None of the four is CORS-safelisted, and the SPA runs on a
        // different origin from the API in every deployed environment, so a header the CORS policy does not
        // name in Access-Control-Expose-Headers is unreadable by the browser — while staying perfectly
        // visible to curl, to the network tab, and to every other assertion in this class, which is not a
        // browser and sends no Origin. The user-visible symptom is a 429 with no idea when to come back, and
        // nothing anywhere reporting a fault.
        //
        // Asserted against the registered policy rather than a response because appsettings.Test.json
        // configures no allowed origins, so the CORS middleware never emits the header here.
        var policy = this.corsOptions.GetPolicy(this.corsOptions.DefaultPolicyName);

        policy.Should().NotBeNull();
        policy!.ExposedHeaders.Should().Contain(RateLimitHeaders.All);
    }

    // ---- The wire contract --------------------------------------------------------------------------

    [Fact]
    public async Task ShouldAnswerTheStandardErrorEnvelope_WithoutEchoingTheRequest()
    {
        for (var i = 0; i < 5; i++)
        {
            await this.GetAsync(ReadRoute);
        }

        var response = await this.GetAsync(ReadRoute);
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // The SPA's axios interceptor dispatches on response.data.error — this string is a contract with
        // client/src/helpers/authFailure.ts.
        root.GetProperty("error").GetString().Should().Be("RATE_LIMITED");
        root.GetProperty("retryAfterSeconds").GetInt32().Should().BeGreaterThan(0);
        root.TryGetProperty("message", out _).Should().BeTrue();

        // Nothing the caller chose comes back. An error body that echoed the path or the claimed address
        // would be a reflection point in a response served before authentication.
        body.Should().NotContain(ReadRoute);
        body.Should().NotContain("203.0.113");
    }

    // ---- AC6: the trail, without flooding it --------------------------------------------------------

    [Fact]
    public async Task ShouldWriteExactlyOneAuditRow_ForABurst()
    {
        // A flood is the entire scenario, so one row per rejection would hand the caller the same power over
        // audit_logs that BLUM-7240 refused them — and a flood is far cheaper to send than a hostile payload.
        // Only the first rejection in a partition's window is a candidate; the rest are already counted onto
        // that row.
        for (var i = 0; i < 35; i++)
        {
            await this.GetAsync(ReadRoute, ip: "203.0.113.50");
        }

        var rows = await this.RateLimitAuditRowsAsync();

        rows.Should().HaveCount(1);

        var payload = Payload(rows[0]);
        payload["rejectionsInWindow"]!.GetValue<int>().Should().Be(1, "the row stands for the burst rather than for one refusal");
        payload["policy"]!.GetValue<string>().Should().Be("Read");
        payload["ip"]!.GetValue<string>().Should().Be("203.0.113.50");
    }

    [Fact]
    public async Task ShouldWriteNoAuditRow_WhenAuditingIsDisabled()
    {
        this.settings.RateLimiting.AuditRateLimitEvents = false;

        for (var i = 0; i < 8; i++)
        {
            await this.GetAsync(ReadRoute);
        }

        (await this.RateLimitAuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldWriteNoAuditRow_WhenNothingWasRejected()
    {
        await this.GetAsync(ReadRoute);
        await this.GetAsync(ReadRoute);

        (await this.RateLimitAuditRowsAsync()).Should().BeEmpty("an allowed request is not a security event");
    }

    // ---- The global kill switch ---------------------------------------------------------------------

    [Fact]
    public async Task ShouldNotLimitAnything_WhenDisabled()
    {
        // Guards the neutralisation the rest of the suite depends on. If this ever fails, every other test
        // class is about to start flaking on a 429 it never asked for.
        this.settings.RateLimiting.Enabled = false;

        for (var i = 0; i < 30; i++)
        {
            (await this.GetAsync(ReadRoute)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var response = await this.GetAsync(ReadRoute);

        response.Headers.Contains("RateLimit-Limit").Should().BeFalse();
    }

    /// <summary>
    /// audit_logs.After is jsonb, so PostgreSQL reformats and reorders what was written — substring matching
    /// against the serialiser's output looks like it works and then fails on the whitespace the database
    /// chose. Parse it.
    /// </summary>
    private static JsonNode Payload(AuditLog row)
    {
        return JsonNode.Parse(row.After!)!;
    }

    private static int? HeaderInt(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), out var parsed) ? parsed : null;
    }

    private Task<HttpResponseMessage> GetAsync(string url, string? ip = null)
    {
        return this.SendAsync(HttpMethod.Get, url, ip, content: null);
    }

    private Task<HttpResponseMessage> PostAsync(string url, string json, string? ip = null)
    {
        return this.SendAsync(HttpMethod.Post, url, ip, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? ip, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, url);

        if (ip is not null)
        {
            // Set per request rather than on the client's default headers. Microsoft.AspNetCore.TestHost
            // leaves Connection.RemoteIpAddress null, so without this every request in the suite resolves to
            // the same "unknown" partition — fine for "a burst is refused", useless for anything per-address.
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        }

        request.Content = content;

        return await this.Client.SendAsync(request);
    }

    private async Task<List<AuditLog>> RateLimitAuditRowsAsync()
    {
        return await this.Context.AuditLogs
            .AsNoTracking()
            .Where(row => row.Action == AuditAction.SecurityRateLimited)
            .ToListAsync();
    }

    private async Task ClearAuditLogsAsync()
    {
        this.Context.AuditLogs.RemoveRange(this.Context.AuditLogs);
        await this.SaveChangesAsync();
    }
}