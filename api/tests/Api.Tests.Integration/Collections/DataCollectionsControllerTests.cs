using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Collections;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Collections;

public class DataCollectionsControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "data-collections";

    private Guid _formModuleId;

    public DataCollectionsControllerTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
    }

    // Minimal response projections so Newtonsoft never has to deserialise the DateOnly columns on CollectionDto.
    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    private sealed record SummaryResult(Guid Id, string Name, string Status);

    public async Task InitializeAsync()
    {
        // The authenticated admin id (AdminsData.MainAdmin) must exist as a real users row because handlers
        // persist it as the audit actor / created-by.
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        Context.Collections.RemoveRange(Context.Collections);
        await SaveChangesAsync();

        _formModuleId = (await Context.FormModules.AsNoTracking().FirstAsync()).Id.Value;

        AuthenticateAsAdmin();
    }

    public Task DisposeAsync()
    {
        AuthenticateAsAdmin();
        return Task.CompletedTask;
    }

    private CreateCollectionRequestDto DraftRequest(string name) => new(
        Name: name,
        Purpose: "Test purpose",
        Type: "quarterly_report",
        FinancialYear: "2026/27",
        Quarter: 1,
        WindowSameAsReportingPeriod: true,
        OpenDate: null,
        Deadline: null,
        IsSubmittedByAllLas: true,
        SaveAsDraft: true,
        LocalAuthorityIds: null,
        FormModuleIds: new[] { _formModuleId },
        Reminders: null);

    private CreateCollectionRequestDto OpenRequest(string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new CreateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            OpenDate: today.AddDays(-5),
            Deadline: today.AddDays(25),
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            LocalAuthorityIds: null,
            FormModuleIds: new[] { _formModuleId },
            Reminders: null);
    }

    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        return dto.Id;
    }

    [Fact]
    public async Task Create_AsDraft_PersistsCollectionAndReturnsDraftStatus()
    {
        var response = await Client.PostAsJsonAsync(Route, DraftRequest("2026 Q1 quarterly report"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.Name.Should().Be("2026 Q1 quarterly report");
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().Be("draft");

        var persisted = await Context.Collections.AsNoTracking().Include(c => c.Forms).SingleAsync(c => c.Id == new CollectionId(dto.Id));
        persisted.IsDraft.Should().BeTrue();
        persisted.Forms.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_PublishedWithinWindow_DerivesOpenStatus()
    {
        var response = await Client.PostAsJsonAsync(Route, OpenRequest("Open collection"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeFalse();
        dto.Status.Should().Be("open");
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await CreateAsync(DraftRequest("Duplicate name"));

        var response = await Client.PostAsJsonAsync(Route, DraftRequest("Duplicate name"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_PublishWithoutFormModules_ReturnsBadRequest()
    {
        // A draft may omit form modules ("save and come back later"), but publishing (SaveAsDraft=false)
        // requires at least one form module. FluentValidation failures surface as 400 Bad Request.
        var request = OpenRequest("No forms") with { FormModuleIds = Array.Empty<Guid>() };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ReturnsCreatedCollection()
    {
        var id = await CreateAsync(DraftRequest("Fetch me"));

        var response = await Client.GetAsync($"{Route}/{id}");

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.Id.Should().Be(id);
        dto.Name.Should().Be("Fetch me");
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"{Route}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_IncludesActive_AndExcludesDeleted()
    {
        var keepId = await CreateAsync(DraftRequest("Keep me"));
        var deleteId = await CreateAsync(DraftRequest("Delete me"));
        (await Client.DeleteAsync($"{Route}/{deleteId}")).IsSuccessStatusCode.Should().BeTrue();

        var response = await Client.GetAsync(Route);

        response.IsSuccessStatusCode.Should().BeTrue();
        var list = await response.ToResponseModel<List<SummaryResult>>();
        list.Should().Contain(c => c.Id == keepId);
        list.Should().NotContain(c => c.Id == deleteId);
    }

    [Fact]
    public async Task Update_ChangesNameAndPersists()
    {
        var id = await CreateAsync(DraftRequest("Original name"));

        var update = new UpdateCollectionRequestDto(
            Name: "Updated name",
            Purpose: "Updated purpose",
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: true,
            CollectionOpenDate: null,
            SubmissionDeadline: null,
            IsSubmittedByAllLas: true,
            SaveAsDraft: true,
            FormModuleIds: new[] { _formModuleId },
            LocalAuthorityIds: null,
            Reminders: null);

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", update);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.Name.Should().Be("Updated name");

        var persisted = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id));
        persisted.Name.Should().Be("Updated name");
    }

    [Fact]
    public async Task Update_WindowSameAsReportingPeriod_AndPublishing_Succeeds()
    {
        // Regression #1: publishing (SaveAsDraft=false) an update whose window matches the reporting period
        // must succeed even with null explicit window dates — the handler derives them from the period.
        var id = await CreateAsync(DraftRequest("Window same as period"));

        var update = new UpdateCollectionRequestDto(
            Name: "Window same as period",
            Purpose: "Test purpose",
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: true,
            CollectionOpenDate: null,
            SubmissionDeadline: null,
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            FormModuleIds: new[] { _formModuleId },
            LocalAuthorityIds: null,
            Reminders: null);

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", update);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeFalse();
    }

    [Fact]
    public async Task Create_DraftWithoutFormModulesOrLas_Succeeds()
    {
        // Regression #2: an early draft can be saved before any form modules or Local Authorities are chosen —
        // completeness rules only apply when publishing (SaveAsDraft=false).
        var request = new CreateCollectionRequestDto(
            Name: "Bare draft",
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: true,
            OpenDate: null,
            Deadline: null,
            IsSubmittedByAllLas: true,
            SaveAsDraft: true,
            LocalAuthorityIds: null,
            FormModuleIds: Array.Empty<Guid>(),
            Reminders: null);

        var response = await Client.PostAsJsonAsync(Route, request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Create_DraftBeforeWindowStep_PersistsUnansweredQuestionsAsNull()
    {
        // BLUM-6625: "Save and come back later" on the first wizard step leaves the window and reporting-LA
        // questions unanswered. The draft must save with those questions stored as null — never coerced to
        // "No"/false or backfilled with derived dates — so resuming the draft shows them unanswered.
        var request = new CreateCollectionRequestDto(
            Name: "Step one draft",
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: null,
            OpenDate: null,
            Deadline: null,
            IsSubmittedByAllLas: null,
            SaveAsDraft: true,
            LocalAuthorityIds: Array.Empty<Guid>(),
            FormModuleIds: Array.Empty<Guid>(),
            Reminders: Array.Empty<ReminderRequestDto>());

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().Be("draft");

        var persisted = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(dto.Id));
        persisted.WindowSameAsReportingPeriod.Should().BeNull();
        persisted.CollectionOpenDate.Should().BeNull();
        persisted.SubmissionDeadline.Should().BeNull();
        persisted.IsSubmittedByAllLas.Should().BeNull();
        persisted.ReportingPeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        persisted.ReportingPeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task Create_NameOnlyDraft_Succeeds()
    {
        // BLUM-6625: a draft only requires a name — every other setup question may be left unanswered.
        var request = new CreateCollectionRequestDto(
            Name: "Name-only draft",
            Purpose: null,
            Type: null,
            FinancialYear: null,
            Quarter: null,
            WindowSameAsReportingPeriod: null,
            OpenDate: null,
            Deadline: null,
            IsSubmittedByAllLas: null,
            SaveAsDraft: true,
            LocalAuthorityIds: Array.Empty<Guid>(),
            FormModuleIds: Array.Empty<Guid>(),
            Reminders: Array.Empty<ReminderRequestDto>());

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().Be("draft");

        var persisted = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(dto.Id));
        persisted.Type.Should().BeNull();
        persisted.FinancialYear.Should().BeNull();
        persisted.ReportingPeriodStart.Should().BeNull();
        persisted.ReportingPeriodEnd.Should().BeNull();
    }

    [Fact]
    public async Task Update_DraftClearingAnswers_PersistsNulls()
    {
        // Saving a draft again may clear previously-answered questions back to unanswered.
        var id = await CreateAsync(DraftRequest("Draft losing answers"));

        var update = new UpdateCollectionRequestDto(
            Name: "Draft losing answers",
            Purpose: null,
            Type: null,
            FinancialYear: null,
            Quarter: null,
            WindowSameAsReportingPeriod: null,
            CollectionOpenDate: null,
            SubmissionDeadline: null,
            IsSubmittedByAllLas: null,
            SaveAsDraft: true,
            FormModuleIds: Array.Empty<Guid>(),
            LocalAuthorityIds: Array.Empty<Guid>(),
            Reminders: null);

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", update);

        response.IsSuccessStatusCode.Should().BeTrue();
        var persisted = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id));
        persisted.Type.Should().BeNull();
        persisted.FinancialYear.Should().BeNull();
        persisted.WindowSameAsReportingPeriod.Should().BeNull();
        persisted.IsSubmittedByAllLas.Should().BeNull();
        persisted.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task Update_PublishWithUnansweredQuestions_ReturnsBadRequest()
    {
        // Publishing (SaveAsDraft=false) still requires every setup question to be answered.
        var id = await CreateAsync(DraftRequest("Incomplete publish"));

        var update = new UpdateCollectionRequestDto(
            Name: "Incomplete publish",
            Purpose: null,
            Type: null,
            FinancialYear: null,
            Quarter: null,
            WindowSameAsReportingPeriod: null,
            CollectionOpenDate: null,
            SubmissionDeadline: null,
            IsSubmittedByAllLas: null,
            SaveAsDraft: false,
            FormModuleIds: Array.Empty<Guid>(),
            LocalAuthorityIds: Array.Empty<Guid>(),
            Reminders: null);

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Close_OpenCollection_SetsClosedStatus()
    {
        var id = await CreateAsync(OpenRequest("Closing collection"));

        var response = await Client.PostAsync($"{Route}/{id}/close", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.Status.Should().Be("closed");
    }

    [Fact]
    public async Task RevertToDraft_OpenCollection_ReturnsToDraft()
    {
        var id = await CreateAsync(OpenRequest("Reverting collection"));

        var response = await Client.PostAsync($"{Route}/{id}/revert-to-draft", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<CollectionResult>();
        dto.IsDraft.Should().BeTrue();
        dto.Status.Should().Be("draft");
    }

    [Fact]
    public async Task Delete_ThenRestore_MovesBetweenMainAndDeletedLists()
    {
        var id = await CreateAsync(DraftRequest("Round trip"));

        (await Client.DeleteAsync($"{Route}/{id}")).IsSuccessStatusCode.Should().BeTrue();

        var deletedList = await (await Client.GetAsync($"{Route}/deleted")).ToResponseModel<List<SummaryResult>>();
        deletedList.Should().Contain(c => c.Id == id);

        (await Client.PostAsync($"{Route}/{id}/restore", null)).IsSuccessStatusCode.Should().BeTrue();

        var mainList = await (await Client.GetAsync(Route)).ToResponseModel<List<SummaryResult>>();
        mainList.Should().Contain(c => c.Id == id);
        var deletedAfter = await (await Client.GetAsync($"{Route}/deleted")).ToResponseModel<List<SummaryResult>>();
        deletedAfter.Should().NotContain(c => c.Id == id);
    }

    [Fact]
    public async Task SendReminder_ForOpenCollection_Succeeds()
    {
        var id = await CreateAsync(OpenRequest("Reminder collection"));

        var response = await Client.PostAsync($"{Route}/{id}/send-reminder", null);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task List_AsNonAdmin_IsForbidden()
    {
        AuthenticateAs(Role.Lead, Guid.NewGuid(), Guid.NewGuid());

        var response = await Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_MaturityAssessment_DerivesFullFinancialYearPeriod()
    {
        // Regression #7: a maturity assessment carries no quarter and covers the whole financial year.
        var request = new CreateCollectionRequestDto(
            Name: "Maturity 2026/27",
            Purpose: null,
            Type: "maturity_assessment",
            FinancialYear: "2026/27",
            Quarter: null,
            WindowSameAsReportingPeriod: true,
            OpenDate: null,
            Deadline: null,
            IsSubmittedByAllLas: true,
            SaveAsDraft: true,
            LocalAuthorityIds: null,
            FormModuleIds: new[] { _formModuleId },
            Reminders: null);

        var id = await CreateAsync(request);

        var persisted = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id));
        persisted.Quarter.Should().BeNull();
        persisted.ReportingPeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        persisted.ReportingPeriodEnd.Should().Be(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public async Task Create_MaturityAssessmentWithQuarter_ReturnsBadRequest()
    {
        // Regression #7: a quarter on a maturity assessment is rejected.
        var request = DraftRequest("Maturity with quarter") with { Type = "maturity_assessment", Quarter = 2 };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WithZeroDayReminder_Succeeds()
    {
        // Regression #10: a reminder of 0 days (send on the first/last day) is accepted on update.
        var id = await CreateAsync(DraftRequest("Zero-day reminder"));

        var update = new UpdateCollectionRequestDto(
            Name: "Zero-day reminder",
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: true,
            CollectionOpenDate: null,
            SubmissionDeadline: null,
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            FormModuleIds: new[] { _formModuleId },
            LocalAuthorityIds: null,
            Reminders: new[] { new ReminderRequestDto("after_open", 0) });

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", update);

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}