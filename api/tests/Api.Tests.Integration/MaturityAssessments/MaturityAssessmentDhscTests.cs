using System.Net;
using Api.Dtos;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// The DHSC side (BLUM-7855 AC4, BLUM-7859 AC2–AC4): every LA's status for a maturity window, one LA's assessment
// read inline, and the consolidated and per-LA downloads — all on the existing data-collections routes.
public class MaturityAssessmentDhscTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private Task<HttpResponseMessage> GetStatusesAsync(Guid collectionId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions");

    private Task<HttpResponseMessage> GetDetailAsync(Guid collectionId, Guid organisationId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/{organisationId}");

    private Task<HttpResponseMessage> GetWindowExportAsync(Guid collectionId, string? region = null) =>
        Client.GetAsync(region is null
            ? $"data-collections/{collectionId}/submissions/export"
            : $"data-collections/{collectionId}/submissions/export?region={Uri.EscapeDataString(region)}");

    private Task<HttpResponseMessage> GetOrgExportAsync(Guid collectionId, Guid organisationId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/{organisationId}/export");

    private static string OneRowSnapshot(string answer) =>
        MaturityAssessmentSnapshotJson.Serialize(new MaturityAssessmentSnapshotDocument(
        [
            new MaturitySnapshotSection(
                "la_capacity",
                "LA capacity",
                [
                    new MaturitySnapshotPage(
                        "people_and_roles",
                        "People and roles",
                        [new MaturitySnapshotRow("MA_DESIGNATED_LEAD", "Does your public health team currently have designated members responsible for gambling harms prevention?", answer, answer.ToLowerInvariant(), null)]),
                ]),
        ]));

    [Fact]
    public async Task Statuses_ListEveryAssignedLa_AndFollowTheAssessment()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        var before = await (await GetStatusesAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();
        before.CollectionType.Should().Be("maturity_assessment");
        before.FinancialYear.Should().Be(TestFinancialYears.Current);
        before.Quarter.Should().BeNull();
        before.Organisations.Should().HaveCount(2).And.OnlyContain(o => o.Status == "not_started");

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        var submitter = await SeedSubmitterAsync();
        await SeedSubmittedAssessmentAsync(collectionId, SecondOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1), submitter: submitter);

        AuthenticateAsAdmin();
        var after = await (await GetStatusesAsync(collectionId)).ToResponseModel<CollectionSubmissionSummaryDto>();
        after.Organisations.Single(o => o.OrganisationId == MainOrg.Id.Value).Status.Should().Be("in_progress");
        var second = after.Organisations.Single(o => o.OrganisationId == SecondOrg.Id.Value);
        second.Status.Should().Be("completed");
        second.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Detail_CarriesTheSectionStatuses_AndTheSnapshotOnceSubmitted()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        var untouched = await (await GetDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();
        untouched.HasSubmission.Should().BeFalse();
        untouched.CollectionType.Should().Be("maturity_assessment");
        untouched.MaturitySections.Should().HaveCount(MaturityProtocol.SectionCodes.Count).And.OnlyContain(s => s.Status == "not_started");
        untouched.MaturitySnapshot.Should().BeNull();

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteSectionFullyAsync(collectionId, "la_capacity");

        AuthenticateAsAdmin();
        var inProgress = await (await GetDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();
        inProgress.HasSubmission.Should().BeTrue();
        inProgress.Status.Should().Be("in_progress");
        inProgress.MaturitySections!.Single(s => s.Code == "la_capacity").Status.Should().Be("completed");
        inProgress.MaturitySnapshot.Should().BeNull("nothing is frozen until submit");

        var submitter = await SeedSubmitterAsync("Julie", "Smith");
        await SeedSubmittedAssessmentAsync(collectionId, SecondOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1), OneRowSnapshot("Yes"), submitter);

        var submitted = await (await GetDetailAsync(collectionId, SecondOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();
        submitted.Status.Should().Be("completed");
        submitted.ReferenceNumber.Should().Be(TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1));
        submitted.SubmittedByName.Should().Contain("Julie");
        submitted.SubmittedByEmail.Should().Be(submitter.Email);
        submitted.MaturitySections.Should().OnlyContain(s => s.Status == "completed");
        submitted.MaturitySnapshot.Should().ContainSingle().Which.Pages[0].Rows.Should().ContainSingle(r => r.Code == "MA_DESIGNATED_LEAD" && r.Answer == "Yes");
    }

    // BLUM-9018: the Files caption is measured through the call the per-organisation download makes, so it is that
    // file's byte length exactly — and absent for an organisation that has not submitted.
    [Fact]
    public async Task Detail_CarriesTheDownloadsByteLength_OnceSubmitted()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedAssessmentAsync(collectionId, MainOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1), OneRowSnapshot("Yes"));

        AuthenticateAsAdmin();
        var submitted = await (await GetDetailAsync(collectionId, MainOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();
        var download = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        var file = await download.Content.ReadAsByteArrayAsync();
        file.Should().NotBeEmpty();
        submitted.ExportSizeBytes.Should().Be(file.LongLength);

        var untouched = await (await GetDetailAsync(collectionId, SecondOrg.Id.Value)).ToResponseModel<OrgSubmissionDetailDto>();
        untouched.ExportSizeBytes.Should().BeNull();
    }

    [Fact]
    public async Task Detail_IsNotFound_ForAnOrganisationOutsideTheWindow()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        (await GetDetailAsync(collectionId, SecondOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConsolidatedExport_IsOneWideRowPerSubmitter_AndIsAudited()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId, ("MA_PRIORITIES", "Licensing first"));
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();
        await SeedSubmittedAssessmentAsync(collectionId, SecondOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 2), OneRowSnapshot("No"));

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().StartWith("maturity-assessments-");
        (await ReadZipEntryNamesAsync(response)).Should().BeEquivalentTo(["summary.csv", "maturity-assessment.csv"]);

        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().StartWith("Organisation,Region,ONS code,E-code,Status,Reference,Submitted by,Submitted on,Financial year");
        summary.Should().Contain($"{MainOrg.Name},{MainOrg.Region},{MainOrg.OnsCode},{MainOrg.ECode},Completed,{TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1)},");
        summary.Should().Contain($"{SecondOrg.Name},{SecondOrg.Region},{SecondOrg.OnsCode},{SecondOrg.ECode},Completed,{TestFinancialYears.MaturityReference(TestFinancialYears.Current, 2)},");
        summary.TrimEnd().Split("\r\n").Should().OnlyContain(line => line.EndsWith("," + TestFinancialYears.Current) || line.EndsWith("Financial year"));

        var wide = await ReadZipEntryAsync(response, "maturity-assessment.csv");
        var lines = wide.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("Organisation,Region,ONS code,E-code,");
        lines[0].Should().Contain("Role 1: role,Role 1: time");
        lines[0].Should().Contain("Who did you engage with when creating your needs assessment? – Police");
        lines.Should().HaveCount(3);
        lines.Should().Contain(l => l.StartsWith(MainOrg.Name + ",") && l.Contains("Public Health Officer") && l.Contains("Licensing first"));
        lines.Should().Contain(l => l.StartsWith(SecondOrg.Name + ",") && l.Contains(",No,"));

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.MaturityAssessmentExported && a.TargetId == collectionId)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task ConsolidatedExport_NarrowsToARegion_AndRefusesAnUnknownOne()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedAssessmentAsync(collectionId, MainOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1), OneRowSnapshot("Yes"));
        await SeedSubmittedAssessmentAsync(collectionId, SecondOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 2), OneRowSnapshot("No"));

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, MainOrg.Region.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(MainOrg.Name);
        summary.Should().NotContain(SecondOrg.Name, "the two fixtures sit in different regions");
        (await ReadZipEntryAsync(response, "maturity-assessment.csv")).Should().NotContain(SecondOrg.Name);

        (await GetWindowExportAsync(collectionId, "Atlantis")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PerOrganisationExport_ServesTheSubmittedAssessment_And404sOtherwise()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedAssessmentAsync(collectionId, MainOrg, TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1), OneRowSnapshot("Yes"));

        AuthenticateAsAdmin();
        var response = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipEntryNamesAsync(response)).Should().BeEquivalentTo(["assessment-summary.csv", "maturity-assessment.csv"]);
        (await ReadZipEntryAsync(response, "maturity-assessment.csv")).Should().Contain($"{MainOrg.Name},LA capacity,People and roles,");

        (await GetOrgExportAsync(collectionId, SecondOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheDhscSurface_IsForbidden_ForLaRoles()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        (await GetStatusesAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetWindowExportAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}