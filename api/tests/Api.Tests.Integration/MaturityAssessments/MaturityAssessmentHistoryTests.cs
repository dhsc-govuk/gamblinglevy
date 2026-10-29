using System.Net;
using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// The LA's own history (BLUM-7859 AC1): maturity windows sit alongside quarterly reports on the shared
// quarterly-report/collections list and the per-collection header, typed so the client can route to the right
// read-only view.
public class MaturityAssessmentHistoryTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private Task<HttpResponseMessage> GetMyCollectionsAsync() =>
        Client.GetAsync("quarterly-report/collections");

    private Task<HttpResponseMessage> GetMyDetailAsync(Guid collectionId) =>
        Client.GetAsync($"quarterly-report/submissions/{collectionId}");

    [Fact]
    public async Task History_ListsTheMaturityWindow_TypedAndByFinancialYear()
    {
        var quarterlyId = await CreateOpenCollectionAsync();
        var maturityId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        var rows = await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>();

        rows.Select(r => r.CollectionId).Should().BeEquivalentTo([quarterlyId, maturityId]);
        var maturity = rows.Single(r => r.CollectionId == maturityId);
        maturity.Type.Should().Be("maturity_assessment");
        maturity.Quarter.Should().BeNull();
        maturity.FinancialYear.Should().Be(TestFinancialYears.Current);
        maturity.SubmissionStatus.Should().Be("not_started");
    }

    [Fact]
    public async Task History_FollowsTheAssessmentStatus()
    {
        var maturityId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(maturityId);
        (await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>())
            .Single().SubmissionStatus.Should().Be("in_progress");

        await CompleteAllSectionsAsync(maturityId);
        var result = await (await SubmitAssessmentAsync(maturityId)).ToResponseModel<SubmitMaturityAssessmentResultDto>();

        var row = (await (await GetMyCollectionsAsync()).ToResponseModel<List<MyCollectionSummaryDto>>()).Single();
        row.SubmissionStatus.Should().Be("completed");
        row.SubmittedAt.Should().NotBeNull();

        var detail = await (await GetMyDetailAsync(maturityId)).ToResponseModel<MySubmissionDetailDto>();
        detail.Type.Should().Be("maturity_assessment");
        detail.FinancialYear.Should().Be(TestFinancialYears.Current);
        detail.HasSubmission.Should().BeTrue();
        detail.Status.Should().Be("completed");
        detail.ReferenceNumber.Should().Be(result.ReferenceNumber);
        detail.LocalAuthorityName.Should().Be(MainOrg.Name);
    }

    // BLUM-9018: the Files caption ("ZIP, 12.4 KB") is measured through the call the LA's own download makes, so it is
    // that file's byte length exactly — and absent while the assessment is still open.
    [Fact]
    public async Task Detail_CarriesTheDownloadsByteLength_OnceSubmitted()
    {
        var maturityId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(maturityId);
        var inProgress = await (await GetMyDetailAsync(maturityId)).ToResponseModel<MySubmissionDetailDto>();
        inProgress.ExportSizeBytes.Should().BeNull("there is no file until the assessment is submitted");

        await CompleteAllSectionsAsync(maturityId);
        (await SubmitAssessmentAsync(maturityId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await (await GetMyDetailAsync(maturityId)).ToResponseModel<MySubmissionDetailDto>();
        var download = await Client.GetAsync($"maturity-assessment/{maturityId}/export");

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        var file = await download.Content.ReadAsByteArrayAsync();
        file.Should().NotBeEmpty();
        detail.ExportSizeBytes.Should().Be(file.LongLength);
    }

    [Fact]
    public async Task Detail_IsTheHeaderBeforeAnythingIsStarted_And404ForAnotherOrganisation()
    {
        var maturityId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        var detail = await (await GetMyDetailAsync(maturityId)).ToResponseModel<MySubmissionDetailDto>();
        detail.Type.Should().Be("maturity_assessment");
        detail.HasSubmission.Should().BeFalse();
        detail.Status.Should().Be("not_started");

        AuthenticateAsSecondOrgLead();
        (await GetMyDetailAsync(maturityId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}