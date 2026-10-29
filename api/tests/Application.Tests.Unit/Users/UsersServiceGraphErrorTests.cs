using System.Net;
using System.Text;
using Application.Common.Settings;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Graph;
using LanguageExt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace Application.Tests.Unit.Users;

/// <summary>
/// BLUM-8746 — UsersService caught ServiceException, which Graph SDK v5 does not throw for a refused
/// request: it throws ODataError. In FindByEmail that was the only catch, so a refused lookup escaped the
/// create-user handlers as an unhandled exception instead of coming back as a Left like every other call.
/// Catching ODataError turned out not to be the whole of it either: a throttled request ends as a different
/// exception once the SDK stops retrying, and a transport failure is never a Graph answer at all.
///
/// These run the real UsersService against the real GraphServiceClient, with the SDK's default middleware in
/// place and only the last hop — the socket — replaced. So what is under test is what the SDK itself turns a
/// response into, retries included. Integration tests cannot see any of this, because they swap the whole
/// service for InMemoryUsersService, and a stubbed request adapter would only prove the service handles
/// whatever exception the test chose to throw.
///
/// What they do not cover is token acquisition: the client here is anonymous, so a failure to get a token
/// (an expired client secret) is represented only by the transport-failure cases, not reproduced.
/// </summary>
public class UsersServiceGraphErrorTests
{
    private const string UserId = "0f6c2f0e-6f0b-4a53-9d5c-2a8f5a3c1b7e";
    private const string Email = "jane.smith@council.gov.uk";
    private const string RefusalMessage = "Graph said no";

    private static readonly Name UserName = new("Jane", "Smith");

    public static TheoryData<string, Func<UsersService, CancellationToken, Task<bool>>> EveryCall => new()
    {
        { nameof(UsersService.FindByEmail), async (service, token) => (await service.FindByEmail(Email, token)).IsLeft },
        { nameof(UsersService.Add), async (service, token) => (await service.Add(UserName, Email, token)).IsLeft },
        { nameof(UsersService.Update), async (service, token) => (await service.Update(UserId, UserName, Email, false, token)).IsLeft },
        { nameof(UsersService.EnablePasswordLogin), async (service, token) => (await service.EnablePasswordLogin(UserId, UserName, Email, "Temp-0rary!", token)).IsLeft },
        { nameof(UsersService.ResetPassword), async (service, token) => (await service.ResetPassword(UserId, Email, "Temp-0rary!", token)).IsLeft },
        { nameof(UsersService.Enable), async (service, token) => (await service.Enable(UserId, token)).IsLeft },
        { nameof(UsersService.Disable), async (service, token) => (await service.Disable(UserId, token)).IsLeft },
        { nameof(UsersService.HardDelete), async (service, token) => (await service.HardDelete(UserId, token)).IsLeft },
    };

    [Fact]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsTheEmailLookup()
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.BadRequest));

        var result = await service.FindByEmail(Email, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldFindNoAccount_WhenTheEmailLookupMatchesNobody()
    {
        // What Graph actually answers for an address it does not hold: 200 and an empty page, not an error.
        var service = ServiceAnswering(_ => Json(HttpStatusCode.OK, """{"value":[]}"""));

        var result = await service.FindByEmail(Email, CancellationToken.None);

        result.IsRight.Should().BeTrue();
        result.RightToSeq().Head().IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReturnTheAccountId_WhenTheEmailLookupMatches()
    {
        var service = ServiceAnswering(_ => Json(HttpStatusCode.OK, $$"""{"value":[{"id":"{{UserId}}"}]}"""));

        var result = await service.FindByEmail(Email, CancellationToken.None);

        result.IsRight.Should().BeTrue();
        result.RightToSeq().Head().IfNone(string.Empty).Should().Be(UserId);
    }

    [Fact]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsTheCreate()
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.BadRequest));

        var result = await service.Add(UserName, Email, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsTheIdentityReadOfAnUpdate()
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.Forbidden));

        var result = await service.Update(UserId, UserName, Email, passwordLoginEnabled: false, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsThePatchOfAnUpdate()
    {
        var service = ServiceAnswering(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """{"identities":[]}""")
            : Refusal(HttpStatusCode.BadRequest));

        var result = await service.Update(UserId, UserName, Email, passwordLoginEnabled: false, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsEnablingOrDisablingTheAccount(bool enable)
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.Forbidden));

        var result = enable
            ? await service.Enable(UserId, CancellationToken.None)
            : await service.Disable(UserId, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturnTheRefusal_WhenGraphRejectsTheDelete()
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.Forbidden));

        var result = await service.HardDelete(UserId, CancellationToken.None);

        ShouldBeTheRefusal(result, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldTreatTheDeleteAsDone_WhenGraphNoLongerHoldsTheAccount()
    {
        var service = ServiceAnswering(_ => Refusal(HttpStatusCode.NotFound));

        var result = await service.HardDelete(UserId, CancellationToken.None);

        result.IsRight.Should().BeTrue("an account already gone from the directory must not block removing the local row");
    }

    [Fact]
    public async Task ShouldReturnTheFailure_WhenGraphKeepsThrottlingUntilTheSdkGivesUp()
    {
        // A throttled request is not an ODataError by the time it gets here. The SDK's retry middleware
        // re-sends it and, when it runs out of attempts, throws its own exception instead of handing back the
        // last 429 — which is why a catch for ODataError alone let sustained throttling through unhandled.
        // Retry-After: 0 keeps the retries real but instant.
        var requests = 0;
        var service = ServiceAnswering(_ =>
        {
            Interlocked.Increment(ref requests);
            var throttled = Refusal(HttpStatusCode.TooManyRequests);
            throttled.Headers.TryAddWithoutValidation("Retry-After", "0");
            return throttled;
        });

        var result = await service.FindByEmail(Email, CancellationToken.None);

        requests.Should().BeGreaterThan(1, "the request has to have been retried for this to be the give-up path and not a plain refusal");
        result.IsLeft.Should().BeTrue("a lookup Graph never answered is returned to the handler, not thrown past it");
        result.LeftToSeq().Head().Should().NotBeOfType<ODataError>("that is the case the other tests cover");
    }

    [Theory]
    [MemberData(nameof(EveryCall))]
    public async Task ShouldReturnTheFailure_WhenTheRequestNeverReachesGraph(string call, Func<UsersService, CancellationToken, Task<bool>> failed)
    {
        var service = ServiceAnswering(_ => throw new HttpRequestException("No such host is known."));

        (await failed(service, CancellationToken.None)).Should().BeTrue($"{call} must hand a transport failure back as a Left");
    }

    [Theory]
    [MemberData(nameof(EveryCall))]
    public async Task ShouldLetCancellationThrough_WhenTheCallerHasGone(string call, Func<UsersService, CancellationToken, Task<bool>> failed)
    {
        // The one failure that is not returned. The bulk importer tells its per-user ceiling from an abandoned
        // request by which token fired, and it can only do that if the cancellation reaches it.
        var service = ServiceAnswering(_ => Json(HttpStatusCode.OK, "{}"));
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        var act = () => failed(service, gone.Token);

        await act.Should().ThrowAsync<OperationCanceledException>($"{call} must not report a cancelled request as a Graph failure");
    }

    private static void ShouldBeTheRefusal<T>(Either<Exception, T> result, HttpStatusCode status)
    {
        result.IsLeft.Should().BeTrue("a request Graph refuses is returned to the handler, not thrown past it");

        var error = result.LeftToSeq().Head().Should().BeOfType<ODataError>().Subject;
        error.ResponseStatusCode.Should().Be((int)status);
        error.Error!.Message.Should().Be(RefusalMessage);
    }

    private static UsersService ServiceAnswering(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        // GraphClientFactory, not a bare HttpClient: it builds the same middleware pipeline production gets
        // from the GraphServiceClient(credential, scopes) constructor — the retry handler in particular — in
        // front of a final handler that answers from memory.
        var http = GraphClientFactory.Create(finalHandler: new CannedGraph(respond));
        var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var settings = new ApplicationSettings
        {
            Graph = new GraphSettings { Domain = "example.onmicrosoft.com", DefaultUserPassword = "not-a-real-password" },
        };

        return new UsersService(graph, settings, NullLogger<UsersService>.Instance);
    }

    private static HttpResponseMessage Refusal(HttpStatusCode status) =>
        Json(status, $$"""{ "error": { "code": "Request_Refused", "message": "{{RefusalMessage}}" } }""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class CannedGraph(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Two things a real transport does that the tests depend on. It refuses to send for a caller who
            // has already gone — a DELETE has no body to read, so nothing later in the SDK would notice. And it
            // stamps the request on its response, which is where the retry handler reads it from to build the
            // next attempt; without that the handler quietly gives the first 429 straight back.
            cancellationToken.ThrowIfCancellationRequested();

            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}