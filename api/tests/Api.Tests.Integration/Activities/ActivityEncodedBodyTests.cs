using System.Net;
using System.Text;
using System.Text.Json;
using Api.Common;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Activities;

// BLUM-8917: "Check your answers" on an activity sends its body Base64-encoded so DAC's gateway WAF has nothing to
// match. Both writes the wizard uses must decode it to the same activity a plain JSON body would have saved.
public class ActivityEncodedBodyTests(IntegrationTestWebFactory factory) : ActivityTestBase(factory)
{
    private const string Title = "Peer support – “drop-in” sessions (weekly) for 18+";

    private Task<HttpResponseMessage> SendEncodedAsync(HttpMethod method, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);
        var envelope = JsonSerializer.Serialize(new { payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) });
        var request = new HttpRequestMessage(method, url) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        request.Headers.Add(AcceptsEncodedJsonBodyAttribute.HeaderName, AcceptsEncodedJsonBodyAttribute.HeaderValue);
        return Client.SendAsync(request);
    }

    [Fact]
    public async Task ShouldCreateAndUpdateAnActivity_WhenTheBodyIsEncoded()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var created = await SendEncodedAsync(
            HttpMethod.Post,
            "activities",
            new CreateActivityRequestDto(Title, categoryId, taskId, null, "planned", null, null, false, DefaultExpectedResults));

        created.IsSuccessStatusCode.Should().BeTrue(await created.Content.ReadAsStringAsync());
        var dto = await created.ToResponseModel<ActivityDto>();

        var updated = await SendEncodedAsync(
            HttpMethod.Put,
            $"activities/{dto.Id}",
            new UpdateActivityRequestDto(Title + " – updated", categoryId, taskId, null, "planned", null, null, false, DefaultExpectedResults));

        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var saved = await Context.Activities.AsNoTracking().FirstAsync(a => a.Id == new ActivityId(dto.Id));
        saved.Title.Should().Be(Title + " – updated");
    }
}