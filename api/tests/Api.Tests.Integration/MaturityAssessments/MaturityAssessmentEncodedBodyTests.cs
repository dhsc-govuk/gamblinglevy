using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Common;
using Api.Dtos;
using Application.MaturityAssessments.Protocol;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// BLUM-8917: DAC's gateway WAF answered 403 to answer saves, so the client Base64-encodes those bodies and
// EncodedJsonBodyMiddleware decodes them before model binding. These pin that an encoded save lands exactly as the
// plain one would, that the sanitiser still sees the decoded strings, and that the envelope is only honoured where
// [AcceptsEncodedJsonBody] opts in.
public class MaturityAssessmentEncodedBodyTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    // The funded-roles page payload from the ticket — the escaped JSON in MA_FUNDED_ROLES is what the WAF refused.
    private const string TicketRolesJson = "[{\"role\":\"public_health_specialist\",\"time\":\"up_to_20\",\"otherRole\":null}]";

    private static string AnswersUrl(Guid collectionId) => $"maturity-assessment/{collectionId}/sections/la_capacity/answers";

    private static SaveMaturityAnswersRequestDto FundedRolesPage(string otherRoles) =>
        new("funded_roles", false, [new MaturityAnswerInputDto("MA_OTHER_ROLES", otherRoles), new MaturityAnswerInputDto("MA_FUNDED_ROLES", TicketRolesJson)]);

    private static string Encode(object body) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonSerializerOptions.Web)));

    private Task<HttpResponseMessage> SendEnvelopeAsync(HttpMethod method, string url, string envelopeJson, string encoding = AcceptsEncodedJsonBodyAttribute.HeaderValue)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(AcceptsEncodedJsonBodyAttribute.HeaderName, encoding);
        return Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutEncodedAsync(string url, object body) =>
        SendEnvelopeAsync(HttpMethod.Put, url, JsonSerializer.Serialize(new { payload = Encode(body) }));

    private async Task<Guid> StartedAsync()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await StartAssessmentAsync(collectionId);
        return collectionId;
    }

    [Fact]
    public async Task ShouldSaveTheTicketsFundedRolesPage_WhenTheBodyIsEncoded()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await PutEncodedAsync(AnswersUrl(collectionId), FundedRolesPage("Data analyst – “part time”"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var answers = await GetAnswersAsync(collectionId);
        answers.Should().ContainSingle(a => a.QuestionCode == "MA_OTHER_ROLES").Which.Value.Should().Be("Data analyst – “part time”");
        MaturityRolesAnswer.TryParse(answers.Single(a => a.QuestionCode == "MA_FUNDED_ROLES").Value, out var roles).Should().BeTrue();
        roles.Should().ContainSingle().Which.Role.Should().Be("public_health_specialist");
    }

    [Fact]
    public async Task ShouldStillAcceptPlainJson_OnAnOptedInAction()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await Client.PutAsJsonAsync(AnswersUrl(collectionId), FundedRolesPage("csdcds"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAnswersAsync(collectionId)).Should().Contain(a => a.QuestionCode == "MA_OTHER_ROLES" && a.Value == "csdcds");
    }

    [Fact]
    public async Task ShouldRunTheStringSanitiser_OnTheDecodedBody()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await PutEncodedAsync(AnswersUrl(collectionId), FundedRolesPage("nul\u0000byte"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAnswersAsync(collectionId)).Should().NotContain(a => a.QuestionCode == "MA_OTHER_ROLES");
    }

    [Theory]
    [InlineData("{\"payload\":\"not base64!\"}", "base64")]
    [InlineData("{\"body\":\"e30=\"}", "base64")]
    [InlineData("{\"payload\":123}", "base64")]
    [InlineData("\"e30=\"", "base64")]
    [InlineData("not json", "base64")]
    [InlineData("{\"payload\":\"e30=\"}", "gzip")]
    public async Task ShouldRejectAnEnvelopeNoConformingClientSends(string envelopeJson, string encoding)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SendEnvelopeAsync(HttpMethod.Put, AnswersUrl(collectionId), envelopeJson, encoding);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.ToResponseModel<ValidationErrorEnvelope>();
        envelope.Error.Should().Be(ValidationErrorEnvelope.ValidationFailed);
        envelope.Errors.Should().ContainSingle().Which.Property.Should().Be("Body");
    }

    [Fact]
    public async Task ShouldNotDecode_OnAnActionThatHasNotOptedIn()
    {
        AuthenticateAsAdmin();
        var request = new CreateCollectionRequestDto(
            Name: "Encoded body collection",
            Purpose: "Should never be created",
            Type: "maturity_assessment",
            FinancialYear: TestFinancialYears.Current,
            Quarter: null,
            WindowSameAsReportingPeriod: false,
            OpenDate: Today.AddDays(-1),
            Deadline: Today.AddDays(60),
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            LocalAuthorityIds: [],
            FormModuleIds: MaturityCollectionForms.Ids,
            Reminders: null);

        var response = await SendEnvelopeAsync(HttpMethod.Post, "data-collections", JsonSerializer.Serialize(new { payload = Encode(request) }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Collections.AsNoTracking().AnyAsync(c => c.Name == "Encoded body collection")).Should().BeFalse();
    }
}