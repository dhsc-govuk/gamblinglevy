using Application.Common.Csv;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Infrastructure.Funding;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Persistence;

public static class ConfigurePersistenceServices
{
    extension(IServiceCollection services)
    {
        public void AddPersistenceServices(IConfiguration configuration)
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("DefaultConnection"));
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            services.AddDbContext<ApplicationDbContext>(
                options => options
                    .UseNpgsql(
                        dataSource,
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

            services.AddScoped<ApplicationDbContextInitialiser>();
            services.AddRepositories();
            services.AddScoped<IFundingWindowResolver, FundingWindowResolver>();
            services.AddScoped<IBulkUploadFileParser, CsvParser>();
        }

        private void AddRepositories()
        {
            services.AddScoped<UserRepository>();
            services.AddScoped<IUserRepository>(provider => provider.GetRequiredService<UserRepository>());
            services.AddScoped<IUserQueries>(provider => provider.GetRequiredService<UserRepository>());

            services.AddScoped<IUserProfileRepository, UserProfileRepository>();

            services.AddScoped<OrganisationRepository>();
            services.AddScoped<IOrganisationRepository>(provider => provider.GetRequiredService<OrganisationRepository>());
            services.AddScoped<IOrganisationQueries>(provider => provider.GetRequiredService<OrganisationRepository>());

            services.AddScoped<LocationRepository>();
            services.AddScoped<ILocationRepository>(provider => provider.GetRequiredService<LocationRepository>());
            services.AddScoped<ILocationQueries>(provider => provider.GetRequiredService<LocationRepository>());

            services.AddScoped<OrganisationUserRepository>();
            services.AddScoped<IOrganisationUserRepository>(provider => provider.GetRequiredService<OrganisationUserRepository>());
            services.AddScoped<IOrganisationUserQueries>(provider => provider.GetRequiredService<OrganisationUserRepository>());

            services.AddScoped<LocationUserRepository>();
            services.AddScoped<ILocationUserRepository>(provider => provider.GetRequiredService<LocationUserRepository>());
            services.AddScoped<ILocationUserQueries>(provider => provider.GetRequiredService<LocationUserRepository>());

            services.AddScoped<IFundingWindowRepository, FundingWindowRepository>();
            services.AddScoped<IOrgFundingAllocationRepository, OrgFundingAllocationRepository>();
            services.AddScoped<IOrgNeighbourRepository, OrgNeighbourRepository>();
            services.AddScoped<ISpendReportTotals, SpendReportTotals>();

            services.AddScoped<SubmissionRepository>();
            services.AddScoped<ISubmissionRepository>(provider => provider.GetRequiredService<SubmissionRepository>());
            services.AddScoped<ISubmissionQueries>(provider => provider.GetRequiredService<SubmissionRepository>());
            services.AddScoped<ISubmissionProbe>(provider => provider.GetRequiredService<SubmissionRepository>());

            services.AddScoped<ISubmissionActivityReportRepository, SubmissionActivityReportRepository>();
            services.AddScoped<ISubmissionActivityAnswerRepository, SubmissionActivityAnswerRepository>();
            services.AddScoped<ISubmissionFeedbackAnswerRepository, SubmissionFeedbackAnswerRepository>();
            services.AddScoped<ISubmissionSpendEntryRepository, SubmissionSpendEntryRepository>();
            services.AddScoped<IKeyResultQueries, KeyResultQueries>();

            services.AddScoped<ActivityRepository>();
            services.AddScoped<IActivityRepository>(provider => provider.GetRequiredService<ActivityRepository>());
            services.AddScoped<IActivityQueries>(provider => provider.GetRequiredService<ActivityRepository>());

            services.AddScoped<IActivityAnswerRepository, ActivityAnswerRepository>();
            services.AddScoped<IActivityExpectedResultRepository, ActivityExpectedResultRepository>();

            services.AddScoped<IFormSectionRepository, FormSectionRepository>();
            services.AddScoped<IFormQuestionRepository, FormQuestionRepository>();
            services.AddScoped<IFormSchemaQueries, FormSchemaQueries>();

            services.AddScoped<CategoryRepository>();
            services.AddScoped<ICategoryQueries>(provider => provider.GetRequiredService<CategoryRepository>());

            services.AddScoped<CollectionRepository>();
            services.AddScoped<ICollectionRepository>(provider => provider.GetRequiredService<CollectionRepository>());
            services.AddScoped<ICollectionQueries>(provider => provider.GetRequiredService<CollectionRepository>());

            services.AddScoped<FormModuleRepository>();
            services.AddScoped<IFormModuleRepository>(provider => provider.GetRequiredService<FormModuleRepository>());
            services.AddScoped<IFormModuleQueries>(provider => provider.GetRequiredService<FormModuleRepository>());
        }
    }
}