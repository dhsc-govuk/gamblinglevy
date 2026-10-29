using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Api.Dtos;
using Application.Common.Features;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// BLUM-6101 — access control on the submission surface, asserted as a table rather than one test per endpoint.
//
// The enforcement itself already existed: no endpoint anywhere takes a submission id, every action on
// QuarterlyReportController resolves the organisation from the caller's claim (never the route or body), and
// (collection_id, organisation_id) is a unique index. What did not exist was anything that fails when a future
// endpoint forgets. A per-endpoint test would have covered the six that already had one and missed the same ten
// that were missing then; enumerating the whole surface once means a new action is either added to the table or
// visibly absent from it in review.
//
// Two conventions this class pins deliberately:
//   * A record outside the caller's organisation answers 404, not the 403 BLUM-6101's AC1 asks for. 403 would
//     confirm that the id names a real collection, which is exactly what an LA probing for another LA's ids
//     needs. The client folds both statuses into one "not yours to see" verdict for this reason
//     (helpers/httpErrors.ts, BLUM-7113), so the user-facing behaviour AC1 describes is unchanged.
//   * The role gate — and only the role gate — answers 403. An admin on the LA surface is the one caller whose
//     refusal cannot leak anything, because DHSC may already see every organisation.
public class SubmissionAccessControlTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    // The one endpoint on the LA surface with no collection id in its route, so there is no foreign id to send it.
    // Its org scoping is covered by LaSubmissionHistoryTests.GetMyCollections_ListsOnlyOwnOrgQuarterlyCollections
    // and its role gate by GetMyCollections_AsAdmin_Returns403.
    private static readonly string[] NotCollectionScoped = ["GET quarterly-report/collections"];

    private readonly EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

    // Multipart: the offline upload (BLUM-8310) takes a file, so it is sent a small CSV as multipart form data — a JSON
    // body would be refused by model binding with a 415 before the report's gate ever ran.
    private sealed record ReportRequest(HttpMethod Method, string Path, object? Body = null, bool Multipart = false);

    // Every collection-scoped endpoint on the LA surface, in controller order. Bodies are the minimum that passes
    // validation, because a validator rejection is a 400 that would mask the refusal being asserted — every write
    // command's validator requires the lists to be non-null rather than non-empty, so an omitted list is enough.
    private static IReadOnlyList<ReportRequest> CollectionScopedSurface(Guid collectionId, Guid activityId) =>
    [
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/context"),
        new(HttpMethod.Get, $"quarterly-report/submissions/{collectionId}"),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/submission"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/task-list"),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/confirm-activities"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/activities"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/activities/{activityId}/questions"),
        new(
            HttpMethod.Put,
            $"quarterly-report/{collectionId}/activities/{activityId}/answers",
            new SaveActivityReportRequestDto(SaveAsDraft: true)),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/activities/{activityId}/complete"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/funding-spend"),
        new(
            HttpMethod.Put,
            $"quarterly-report/{collectionId}/funding-spend/actuals",
            new SaveFundingActualsRequestDto(SaveAsDraft: true)),
        new(
            HttpMethod.Put,
            $"quarterly-report/{collectionId}/funding-spend/forecast",
            new SaveFundingForecastRequestDto(SaveAsDraft: true)),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/funding-spend/complete"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/strategic-feedback"),
        new(
            HttpMethod.Put,
            $"quarterly-report/{collectionId}/strategic-feedback",
            new SaveStrategicFeedbackRequestDto(SaveAsDraft: true)),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/strategic-feedback/complete"),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/submission/submit"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/export"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/offline/activity-results/template"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/offline/funding-spent/template"),
        new(HttpMethod.Get, $"quarterly-report/{collectionId}/offline/strategic-feedback/template"),
        new(HttpMethod.Post, $"quarterly-report/{collectionId}/offline/upload", Multipart: true),
    ];

    // AC1 + AC2. The arranged report is real — a started submission with its activity list confirmed — so every
    // refusal below means "not your organisation's", not "nothing to find". Without that arrangement the same
    // assertions would pass against an API with no scoping at all.
    [Fact]
    public async Task ShouldRefuseTheWholeReportSurface_ForAnotherOrganisationsCollection()
    {
        // The offline routes (BLUM-8310) are on the surface only while their flag is; off, the feature gate answers
        // 404 for everybody, the owner included.
        await factory.SetFeatureAsync(Feature.BLUM_8310, true);

        // Arrange — SecondOrg's live report. Identity is switched here rather than inside a helper: assignments to
        // the AsyncLocal identity made inside an async method do not flow back to its caller.
        AuthenticateAsSecondOrgLead();
        var collectionId = await CreateOpenCollectionAsync("2026 Q1 second-org report", [SecondOrg.Id.Value]);
        var activityId = (await CreateRnaActivityAsync()).Id;
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        // The owning organisation reads every one of them successfully. Without this leg the assertion below would
        // hold just as well against an API that answered 404 for everybody — which is the failure mode a table of
        // expected refusals is most likely to rot into. The export is excluded because it 404s until the report is
        // submitted, which is a state rule rather than an access one (SubmitReportTests covers it, and the third
        // test in this class covers the submitted case).
        var readsAsOwner = CollectionScopedSurface(collectionId, activityId)
            .Where(request => request.Method == HttpMethod.Get && !request.Path.EndsWith("/export"))
            .ToList();
        (await MismatchesAsync(HttpStatusCode.OK, readsAsOwner)).Should().BeEmpty(
            "the organisation the collection belongs to must be able to read its own report");

        // Act
        AuthenticateAsMainOrgLead();
        var mismatches = await MismatchesAsync(HttpStatusCode.NotFound, CollectionScopedSurface(collectionId, activityId));

        // Assert
        mismatches.Should().BeEmpty(
            "every endpoint on the LA report surface must refuse a collection belonging to another organisation");
    }

    // The one place an id from outside the caller's organisation can still be supplied once the collection gate has
    // passed: the caller's own open collection with somebody else's activity in the route. The refusal must be
    // indistinguishable from the one a made-up id gets, or the difference is a probe that confirms another LA's
    // activity ids. The status here is a 422 rather than a 404 (SubmissionActivityNotReportableException) — which is
    // fine precisely because both cases produce it, with the same "it does not exist" reason.
    [Fact]
    public async Task ShouldNotDistinguishAnotherOrganisationsActivity_FromOneThatDoesNotExist()
    {
        // Arrange — an activity owned by SecondOrg…
        AuthenticateAsSecondOrgLead();
        await CreateOpenCollectionAsync("2026 Q1 second-org report", [SecondOrg.Id.Value]);
        var foreignActivityId = (await CreateRnaActivityAsync()).Id;

        // …and MainOrg's own live report, which the caller is fully entitled to write to.
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var ownActivityId = (await CreateRnaActivityAsync()).Id;
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var unknownActivityId = Guid.NewGuid();

        // Act
        var foreignActivity = await ActivityEndpointOutcomesAsync(collectionId, foreignActivityId);
        var unknownActivity = await ActivityEndpointOutcomesAsync(collectionId, unknownActivityId);

        // Assert — the caller's own activity is reachable on the same route, so the refusals are not vacuous. Only
        // the read is exercised for this leg: "complete" validates the saved answers and would 422 on an activity
        // nobody has answered yet, which says nothing about access.
        var own = await Client.GetAsync($"quarterly-report/{collectionId}/activities/{ownActivityId}/questions");
        own.StatusCode.Should().Be(HttpStatusCode.OK, "the caller's own activity must be reachable on this route");

        // Another organisation's activity is answered exactly as a non-existent one is. The activity id is masked
        // out of the comparison because it is echoed back in the message and is the one thing that must differ.
        foreignActivity.Should().Equal(
            unknownActivity,
            "another organisation's activity id must be answered exactly as an id that names nothing");
    }

    // The highest-value case in the ticket: the finalised ZIP of another organisation's report genuinely exists on
    // disk by this point, so the refusal is about ownership rather than about there being nothing to send.
    [Fact]
    public async Task ShouldRefuseTheReportDownload_ForAnotherOrganisationsSubmittedReport()
    {
        // Arrange — SecondOrg completes and submits a whole report.
        AuthenticateAsSecondOrgLead();
        var collectionId = await CreateOpenCollectionAsync("2026 Q1 second-org report", [SecondOrg.Id.Value]);
        var activityId = (await CreateRnaActivityAsync()).Id;
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activityId);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await Client.PostAsync($"quarterly-report/{collectionId}/submission/submit", null)).EnsureSuccessStatusCode();

        var owner = await Client.GetAsync($"quarterly-report/{collectionId}/export");
        owner.StatusCode.Should().Be(HttpStatusCode.OK, "the organisation that submitted it must be able to download it");

        // Act
        AuthenticateAsMainOrgLead();
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/zip");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(SecondOrg.Name);
    }

    // AC3, from the other direction. DHSC reads submissions through its own admin surface; the LA authoring surface
    // is closed to it, which is what stops a DHSC role ever reaching a mutation endpoint at all — there is no
    // verification, return or edit endpoint for it to be refused from, because none was ever built.
    [Fact]
    public async Task ShouldForbidDhscAdmin_OnTheWholeLaReportSurface()
    {
        // With the flag off the feature gate answers the offline routes' 404 before the role gate is reached.
        await factory.SetFeatureAsync(Feature.BLUM_8310, true);

        // Arrange — a live report that a permitted LA caller can drive, so the refusals are about the role.
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activityId = (await CreateRnaActivityAsync()).Id;
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        // Act
        AuthenticateAsAdmin();
        var surface = CollectionScopedSurface(collectionId, activityId)
            .Append(new ReportRequest(HttpMethod.Get, "quarterly-report/collections"))
            .ToList();
        var mismatches = await MismatchesAsync(HttpStatusCode.Forbidden, surface);

        // Assert
        mismatches.Should().BeEmpty("the LA authoring surface is closed to DHSC roles by the class-level role gate");
    }

    // The table above is hand-written, and the class comment claims a new endpoint is "either added to the table or
    // visibly absent from it in review" — which nothing enforced, so a future collection-scoped action that resolved
    // the organisation from the route would simply never be sent and every test here would stay green.
    //
    // It checks both directions, and the second is the one that matters most: a *typo* in a table path makes that
    // request 404 because the route does not exist, which is the status ShouldRefuse… asserts, so a mistyped entry
    // passes while testing nothing at all. Comparing against the real endpoint table is what makes that impossible.
    [Fact]
    public void ShouldSendEveryCollectionScopedEndpointOnTheLaSurface()
    {
        var declared = CollectionScopedSurface(Guid.NewGuid(), Guid.NewGuid())
            .Select(request => $"{request.Method} {Shape(request.Path)}")
            .ToHashSet(StringComparer.Ordinal);

        var live = this.endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} {Shape((endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))}"))
            .Where(route => route.Contains(" quarterly-report/", StringComparison.Ordinal) || route.EndsWith(" quarterly-report", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        live.ExceptWith(NotCollectionScoped);

        live.Except(declared).Should().BeEmpty("every collection-scoped LA endpoint must be exercised by this class");
        declared.Except(live).Should().BeEmpty("a table entry that matches no real route 404s for the wrong reason and proves nothing");
    }

    // Reduces a route template and a concrete request path to the same shape, so the two can be compared: both
    // "{collectionId:guid}" and a substituted GUID collapse to "{}".
    private static string Shape(string route) =>
        Regex.Replace(route, @"\{[^}]*\}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "{}");

    // Returns "{status}|{route}|{body}" for each activity-scoped endpoint, with the two things that legitimately
    // vary between runs masked out: the activity id (echoed back in the failure message, and the very thing the two
    // runs differ by) and the ProblemDetails traceId (a fresh correlation id on every request). Everything else has
    // to match, so a status code, an error code or a message that betrays which of the two ids named a real record
    // fails this test.
    private async Task<IReadOnlyList<string>> ActivityEndpointOutcomesAsync(Guid collectionId, Guid activityId)
    {
        var requests = CollectionScopedSurface(collectionId, activityId)
            .Where(request => request.Path.Contains($"/activities/{activityId}"))
            .ToList();

        var outcomes = new List<string>();
        foreach (var request in requests)
        {
            using var response = await SendAsync(request);
            var body = Mask(await response.Content.ReadAsStringAsync(), activityId);
            outcomes.Add($"{(int)response.StatusCode}|{request.Method} {Mask(request.Path, activityId)}|{body}");
        }

        return outcomes;
    }

    // Sends every request in the table and reports the ones that did not answer as expected. Collecting rather than
    // asserting per request means one run names every endpoint that regressed, not just the first.
    private async Task<IReadOnlyList<string>> MismatchesAsync(HttpStatusCode expected, IReadOnlyList<ReportRequest> requests)
    {
        var mismatches = new List<string>();
        foreach (var request in requests)
        {
            using var response = await SendAsync(request);
            if (response.StatusCode != expected)
            {
                mismatches.Add($"{request.Method} {request.Path} answered {(int)response.StatusCode}, expected {(int)expected}");
            }
        }

        return mismatches;
    }

    private Task<HttpResponseMessage> SendAsync(ReportRequest request)
    {
        var message = new HttpRequestMessage(request.Method, request.Path)
        {
            Content = request.Multipart
                ? OfflineFile()
                : request.Body is null ? null : JsonContent.Create(request.Body, request.Body.GetType()),
        };

        return Client.SendAsync(message);
    }

    private static MultipartFormDataContent OfflineFile()
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Reference,Answer\r\nx,y"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "offline.csv" } };
    }

    private static string Mask(string value, Guid activityId) =>
        Regex.Replace(value.Replace(activityId.ToString(), "{activityId}"), "\"traceId\":\"[^\"]*\"", "\"traceId\":\"{traceId}\"");
}