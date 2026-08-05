using System.Reflection;
using Domain.Activities;
using Domain.AuditLogs;
using Domain.Categories;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.Funding;
using Domain.KeyResults;
using Domain.LocationUsers;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Submissions;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Organisation> Organisations { get; set; }
    public DbSet<Domain.Locations.Location> Locations { get; set; }
    public DbSet<OrganisationUser> OrganisationUsers { get; set; }
    public DbSet<LocationUser> LocationUsers { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<UserMfa> UserMfas { get; set; }
    public DbSet<MfaSession> MfaSessions { get; set; }
    public DbSet<FundingWindow> FundingWindows { get; set; }
    public DbSet<OrgFundingAllocation> OrgFundingAllocations { get; set; }
    public DbSet<OrgNeighbour> OrgNeighbours { get; set; }
    public DbSet<Activity> Activities { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<ActivityTask> ActivityTasks { get; set; }
    public DbSet<FormSection> FormSections { get; set; }
    public DbSet<FormQuestion> FormQuestions { get; set; }
    public DbSet<FormQuestionOption> FormQuestionOptions { get; set; }
    public DbSet<ActivityAnswer> ActivityAnswers { get; set; }
    public DbSet<ActivityExpectedResult> ActivityExpectedResults { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionForm> CollectionForms { get; set; }
    public DbSet<CollectionLocalAuthority> CollectionLocalAuthorities { get; set; }
    public DbSet<CollectionReminder> CollectionReminders { get; set; }
    public DbSet<FormModule> FormModules { get; set; }
    public DbSet<DcFormSection> DcFormSections { get; set; }
    public DbSet<DcFormQuestion> DcFormQuestions { get; set; }
    public DbSet<DcFormQuestionOption> DcFormQuestionOptions { get; set; }
    public DbSet<KeyResultDefinition> KeyResultDefinitions { get; set; }
    public DbSet<KeyResultQuestion> KeyResultQuestions { get; set; }
    public DbSet<Submission> Submissions { get; set; }
    public DbSet<SubmissionSection> SubmissionSections { get; set; }
    public DbSet<SubmissionActivityReport> SubmissionActivityReports { get; set; }
    public DbSet<SubmissionActivityAnswer> SubmissionActivityAnswers { get; set; }
    public DbSet<SubmissionFeedbackAnswer> SubmissionFeedbackAnswers { get; set; }
    public DbSet<SubmissionSpendEntry> SubmissionSpendEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}