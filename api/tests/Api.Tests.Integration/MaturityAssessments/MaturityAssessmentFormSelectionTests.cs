using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// BLUM-8093: the forms a DHSC admin keeps on a maturity collection (BLUM-7943) are the LA's assessment. A section
// left off is not listed, not readable, not writable and not required to submit — and the set is read live from the
// collection, so an admin's later edit reaches an assessment that is already under way.
public class MaturityAssessmentFormSelectionTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private const string LaCapacity = "la_capacity";
    private const string NeedsAssessment = "needs_assessment";
    private const string NextSteps = "next_steps";

    [Fact]
    public async Task ShouldListOnlyTheSelectedSections_InCatalogueOrder()
    {
        // Stored in reverse, so the order the task list returns can only come from the catalogue.
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(NextSteps, LaCapacity));

        AuthenticateAsMainOrgLead();
        var started = await StartAssessmentAsync(collectionId);

        started.Sections.Select(s => s.Code).Should().Equal(LaCapacity, NextSteps);
        started.Sections.Should().OnlyContain(s => s.Status == "not_started");
        started.CanSubmit.Should().BeFalse();

        var reread = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        reread.Sections.Select(s => s.Code).Should().Equal(LaCapacity, NextSteps);

        var saved = await GetAssessmentAsync(collectionId);
        saved.Sections.Select(s => s.SectionCode).Should().BeEquivalentTo([LaCapacity, NextSteps]);
    }

    [Fact]
    public async Task ShouldNotLetTheLaReadOrWriteAnUnselectedSection()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity, NextSteps));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        (await GetSectionResponseAsync(collectionId, NeedsAssessment)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SavePageAsync(collectionId, NeedsAssessment, "publishing", true, ("MA_NA_PUBLISHED", "no")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CompleteSectionAsync(collectionId, NeedsAssessment)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var saved = await GetAssessmentAsync(collectionId);
        saved.Sections.Should().NotContain(s => s.SectionCode == NeedsAssessment);
        (await GetAnswersAsync(collectionId)).Should().NotContain(a => a.QuestionCode == "MA_NA_PUBLISHED");

        // The selected sections are unaffected.
        (await GetSectionResponseAsync(collectionId, LaCapacity)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldNotPrefillAnUnselectedSection()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity));
        await SeedBaselineAnswersAsync(MainOrg, ("MA_DESIGNATED_LEAD", "yes"), ("MA_NA_PUBLISHED", "yes"));

        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.IsPrefilled.Should().BeTrue();
        var answers = await GetAnswersAsync(collectionId);
        answers.Should().Contain(a => a.QuestionCode == "MA_DESIGNATED_LEAD");
        answers.Should().NotContain(a => a.QuestionCode == "MA_NA_PUBLISHED");
    }

    [Fact]
    public async Task ShouldSubmitOnceTheSelectedSectionsAreComplete_AndSnapshotOnlyThose()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity, NextSteps));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        await CompleteSectionFullyAsync(collectionId, LaCapacity);
        var halfway = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        halfway.CanSubmit.Should().BeFalse();

        await CompleteSectionFullyAsync(collectionId, NextSteps);
        var ready = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        ready.CanSubmit.Should().BeTrue();

        // The eight unselected sections hold required questions nobody answered; they must not 422 the submit.
        var response = await SubmitAssessmentAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = await GetAssessmentAsync(collectionId);
        saved.IsSubmitted.Should().BeTrue();
        var snapshot = MaturityAssessmentSnapshotJson.TryRead(saved.SnapshotJson);
        snapshot.Should().NotBeNull();
        snapshot!.Sections.Select(s => s.Code).Should().Equal(LaCapacity, NextSteps);
    }

    [Fact]
    public async Task ShouldDropASectionTheAdminUnticksAfterTheLaStarted()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity, NextSteps));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteSectionFullyAsync(collectionId, LaCapacity);

        await SetCollectionFormsAsync(collectionId, FormIds(LaCapacity));

        AuthenticateAsMainOrgLead();
        var taskList = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        taskList.Sections.Select(s => s.Code).Should().Equal(LaCapacity);
        taskList.CanSubmit.Should().BeTrue();
        (await GetSectionResponseAsync(collectionId, NextSteps)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await SubmitAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldRequireASectionTheAdminTicksAfterTheLaStarted()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteSectionFullyAsync(collectionId, LaCapacity);

        await SetCollectionFormsAsync(collectionId, FormIds(LaCapacity, NextSteps));

        AuthenticateAsMainOrgLead();
        var taskList = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        taskList.Sections.Select(s => (s.Code, s.Status)).Should().Equal((LaCapacity, "completed"), (NextSteps, "not_started"));
        taskList.CanSubmit.Should().BeFalse();

        var refused = await SubmitAssessmentAsync(collectionId);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("REPORT_INCOMPLETE");

        await CompleteSectionFullyAsync(collectionId, NextSteps);
        (await SubmitAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldShowDhscOnlyTheSelectedSections_ForAnLaThatHasNotStarted()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity, NextSteps));

        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"data-collections/{collectionId}/submissions/{MainOrg.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<OrgSubmissionDetailDto>();
        detail.HasSubmission.Should().BeFalse();
        detail.MaturitySections!.Select(s => s.Code).Should().Equal(LaCapacity, NextSteps);
        detail.MaturitySections.Should().OnlyContain(s => s.Status == "not_started");
    }

    // The window stays editable while open, so the admin can change the forms after an LA has submitted. The
    // submitted assessment is frozen: neither the LA's view nor DHSC's may gain a "Not started" section it was never
    // asked, or lose one it completed.
    [Fact]
    public async Task ShouldKeepASubmittedAssessmentsSections_WhenTheAdminChangesTheFormsAfterwards()
    {
        var collectionId = await SubmitThenReplaceLaCapacityWithNeedsAssessmentAsync();

        AuthenticateAsMainOrgLead();
        var taskList = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        taskList.Submitted.Should().BeTrue();
        taskList.Sections.Select(s => (s.Code, s.Status)).Should().Equal((LaCapacity, "completed"), (NextSteps, "completed"));

        AuthenticateAsAdmin();
        var detail = await (await Client.GetAsync($"data-collections/{collectionId}/submissions/{MainOrg.Id.Value}"))
            .ToResponseModel<OrgSubmissionDetailDto>();
        detail.HasSubmission.Should().BeTrue();
        detail.MaturitySections!.Select(s => (s.Code, s.Status)).Should().Equal((LaCapacity, "completed"), (NextSteps, "completed"));
        detail.MaturitySnapshot!.Select(s => s.Code).Should().Equal(LaCapacity, NextSteps);
    }

    // The wide export is filled from each submitter's frozen snapshot, so its columns must still cover a section an
    // LA submitted after the admin unticked it — otherwise those answers silently leave the file. A section that was
    // never selected and never submitted still gets no columns.
    [Fact]
    public async Task ShouldKeepExportColumnsForASubmittedSection_TheAdminUntickedAfterwards()
    {
        var collectionId = await SubmitThenReplaceLaCapacityWithNeedsAssessmentAsync();

        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"data-collections/{collectionId}/submissions/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lines = (await ReadZipEntryAsync(response, "maturity-assessment.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Contain(Label("MA_DESIGNATED_LEAD"), "la_capacity was submitted, then unticked");
        lines[0].Should().Contain(Label("MA_NA_PUBLISHED"), "needs_assessment is one of the window's current forms");
        lines[0].Should().NotContain(Label("MA_LIC_PH_INVOLVED"), "planning_licensing was never selected nor submitted");
        lines.Should().Contain(l => l.StartsWith(MainOrg.Name + ",") && l.Contains(",Yes,"));
    }

    private static string Label(string questionCode) =>
        MaturityQuestionResolver.ResolveAll().Single(q => q.Code == questionCode).Label;

    // MainOrg submits la_capacity + next_steps; the admin then unticks la_capacity and ticks needs_assessment.
    private async Task<Guid> SubmitThenReplaceLaCapacityWithNeedsAssessmentAsync()
    {
        var collectionId = await CreateMaturityCollectionAsync(formIds: FormIds(LaCapacity, NextSteps));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CompleteSectionFullyAsync(collectionId, LaCapacity, ("MA_DESIGNATED_LEAD", "yes"));
        await CompleteSectionFullyAsync(collectionId, NextSteps);
        (await SubmitAssessmentAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetCollectionFormsAsync(collectionId, FormIds(NextSteps, NeedsAssessment));
        return collectionId;
    }

    private static IReadOnlyList<Guid> FormIds(params string[] sectionCodes) =>
        sectionCodes.Select(code => MaturityCollectionForms.All.Single(f => f.SectionCode == code).Id).ToList();

    // The admin editing the Forms step of the published collection — the same PUT CollectionEditor sends, repeating
    // the setup CreateMaturityCollectionAsync published with.
    private async Task SetCollectionFormsAsync(Guid collectionId, IReadOnlyList<Guid> formIds)
    {
        AuthenticateAsAdmin();

        var update = new UpdateCollectionRequestDto(
            Name: "Maturity assessment",
            Purpose: "Maturity assessment tests",
            Type: "maturity_assessment",
            FinancialYear: TestFinancialYears.Current,
            Quarter: null,
            WindowSameAsReportingPeriod: false,
            CollectionOpenDate: Today.AddDays(-1),
            SubmissionDeadline: Today.AddDays(60),
            IsSubmittedByAllLas: false,
            SaveAsDraft: false,
            FormModuleIds: formIds,
            LocalAuthorityIds: [MainOrg.Id.Value],
            Reminders: null);

        var response = await Client.PutAsJsonAsync($"data-collections/{collectionId}", update);
        response.IsSuccessStatusCode.Should().BeTrue("updating the collection's forms failed: {0}", await response.Content.ReadAsStringAsync());
    }
}