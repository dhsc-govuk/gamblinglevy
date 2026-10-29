using System.Net;
using System.Text.RegularExpressions;
using Api.Dtos;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Common.Services;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// Finalising the assessment (BLUM-7858 AC3/AC4) and what the finalised record looks like afterwards (BLUM-7859
// AC1): the reference, the frozen snapshot, the audit row, the confirmation email, the read-only copy and the
// per-LA download.
public class MaturityAssessmentSubmitTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    [Fact]
    public async Task Submit_IsRefused_WhileASectionIsIncomplete()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteSectionFullyAsync(collectionId, "la_capacity");

        var response = await SubmitAssessmentAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("REPORT_INCOMPLETE");
        (await GetAssessmentAsync(collectionId)).IsSubmitted.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_FreezesTheAssessment_AuditsIt_AndEmailsTheSubmitter()
    {
        InMemoryMaturityAssessmentConfirmationEmailService.Clear();
        await EnsureMainOrgLeadUserAsync();
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId);

        var before = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        before.CanSubmit.Should().BeTrue();

        var response = await SubmitAssessmentAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ToResponseModel<SubmitMaturityAssessmentResultDto>();
        result.ReferenceNumber.Should().Be(TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1));
        Regex.IsMatch(result.ReferenceNumber, "^\\d{4}-\\d{4}$").Should().BeTrue();

        var saved = await GetAssessmentAsync(collectionId);
        saved.IsSubmitted.Should().BeTrue();
        saved.ReferenceNumber.Should().Be(result.ReferenceNumber);
        saved.SubmittedBy!.Value.Should().Be(MainOrgLeadUserId);
        var snapshot = MaturityAssessmentSnapshotJson.TryRead(saved.SnapshotJson);
        snapshot.Should().NotBeNull();
        snapshot!.Sections.Select(s => s.Code).Should().Equal(MaturityProtocol.SectionCodes);
        snapshot.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Rows).Should().Contain(r => r.Code == "MA_DESIGNATED_LEAD" && r.Answer == "Yes");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == AuditTargetType.MaturityAssessment && a.TargetId == saved.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(a => a.Action == AuditAction.MaturityAssessmentSubmitted);

        InMemoryMaturityAssessmentConfirmationEmailService.Sent
            .Should().Contain(s => s.Email == MainOrgLeadEmail && s.ReferenceNumber == result.ReferenceNumber);

        var after = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        after.Submitted.Should().BeTrue();
        after.CanSubmit.Should().BeFalse();
        after.ReferenceNumber.Should().Be(result.ReferenceNumber);

        var context = await (await GetContextAsync(collectionId)).ToResponseModel<MaturityAssessmentContextDto>();
        context.Status.Should().Be("completed");
        context.ReferenceNumber.Should().Be(result.ReferenceNumber);
    }

    [Fact]
    public async Task Submit_SequencesTheReferenceAcrossLocalAuthorities()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId);
        var first = await (await SubmitAssessmentAsync(collectionId)).ToResponseModel<SubmitMaturityAssessmentResultDto>();

        AuthenticateAsSecondOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId);
        var second = await (await SubmitAssessmentAsync(collectionId)).ToResponseModel<SubmitMaturityAssessmentResultDto>();

        first.ReferenceNumber.Should().Be(TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1));
        second.ReferenceNumber.Should().Be(TestFinancialYears.MaturityReference(TestFinancialYears.Current, 2));
    }

    [Fact]
    public async Task ASubmittedAssessment_IsLocked()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId);
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();

        (await SubmitAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SavePageAsync(collectionId, "la_capacity", "people_and_roles", true, ("MA_DESIGNATED_LEAD", "no"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CompleteSectionAsync(collectionId, "la_capacity")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StartAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submit_IsRefused_OnceTheWindowHasClosed()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId);
        await CloseCollectionAsync(collectionId);

        AuthenticateAsMainOrgLead();
        var response = await SubmitAssessmentAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("REPORT_NOT_OPEN");
    }

    [Fact]
    public async Task TheSubmittedCopy_OmitsHiddenQuestionsAndPages()
    {
        await EnsureMainOrgLeadUserAsync();
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId, ("MA_NA_PUBLISHED", "no"), ("MA_STRATEGIC_NETWORK", "no"));
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await GetSubmittedAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await response.ToResponseModel<SubmittedMaturityAssessmentDto>();
        submitted.LocalAuthorityName.Should().Be(MainOrg.Name);
        submitted.SubmittedByEmail.Should().Be(MainOrgLeadEmail);
        submitted.Sections.Select(s => s.Code).Should().Equal(MaturityProtocol.SectionCodes);

        var needs = submitted.Sections.Single(s => s.Code == "needs_assessment");
        needs.Pages.Select(p => p.Code).Should().Equal("publishing", "regional_hna");
        needs.Pages[0].Rows.Select(r => r.Code).Should().Equal("MA_NA_PUBLISHED");

        var capacity = submitted.Sections.Single(s => s.Code == "la_capacity");
        capacity.Pages.Single(p => p.Code == "strategic_network").Rows.Select(r => r.Code).Should().Equal("MA_STRATEGIC_NETWORK");
        capacity.Pages.Single(p => p.Code == "funded_roles").Rows.Should().Contain(r => r.Code == "MA_FUNDED_ROLES" && r.Answer.Contains("Public Health Officer"));
    }

    // BLUM-8438, sheet row 44: an LA that answered "Yes", filled in the steering group's members and then changed the
    // answer to "In development" is not asked for them again, and its submitted copy leaves the members page out.
    [Fact]
    public async Task TheSubmittedCopy_OmitsSteeringGroupMembers_OnceTheAnswerChangesToInDevelopment()
    {
        await EnsureMainOrgLeadUserAsync();
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId, ("MA_SG_EXISTS", "yes"));
        (await GetAnswersAsync(collectionId)).Should().Contain(a => a.QuestionCode == "MA_SG_MEMBERS__POLICE");

        (await SavePageAsync(collectionId, "strategic_approach", "steering_group", false, ("MA_SG_EXISTS", "in_development"))).EnsureSuccessStatusCode();
        var complete = await CompleteSectionAsync(collectionId, "strategic_approach");
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();

        var submitted = await (await GetSubmittedAsync(collectionId)).ToResponseModel<SubmittedMaturityAssessmentDto>();
        var strategic = submitted.Sections.Single(s => s.Code == "strategic_approach");
        strategic.Pages.Select(p => p.Code).Should().NotContain("steering_group_members");
        strategic.Pages.SelectMany(p => p.Rows).Should().NotContain(r => r.Code.StartsWith("MA_SG_MEMBERS"));
        strategic.Pages.Single(p => p.Code == "steering_group").Rows.Should().ContainSingle(r => r.Code == "MA_SG_EXISTS" && r.Answer == "In development");
    }

    [Fact]
    public async Task TheDownload_IsAZipOfTheSnapshot_WithFormulasNeutralised()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteAllSectionsAsync(collectionId, ("MA_PRIORITIES", "=cmd()|' /C calc'!A0"));
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await ExportAssessmentAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().EndWith(".zip").And.Contain("maturity-assessment");
        (await ReadZipEntryNamesAsync(response)).Should().BeEquivalentTo(["assessment-summary.csv", "maturity-assessment.csv"]);

        var answers = await ReadZipEntryAsync(response, "maturity-assessment.csv");
        answers.Should().StartWith("Local authority,Section,Page,Question,Answer");
        answers.Should().Contain("cmd()");
        answers.Split("\r\n").Should().NotContain(line => line.Split(',').Any(cell => cell.StartsWith('=')));

        var summary = await ReadZipEntryAsync(response, "assessment-summary.csv");
        summary.Should().Contain("Reference number").And.Contain(TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1));
    }

    [Fact]
    public async Task TheSubmittedCopyAndDownload_Are404_BeforeSubmitAndForAnotherOrganisation()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        (await GetSubmittedAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ExportAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await CompleteAllSectionsAsync(collectionId);
        (await SubmitAssessmentAsync(collectionId)).EnsureSuccessStatusCode();

        AuthenticateAsSecondOrgLead();
        (await GetSubmittedAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ExportAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}