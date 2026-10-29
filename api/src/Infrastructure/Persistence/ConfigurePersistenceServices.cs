using Application.Common.Csv;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Infrastructure.Funding;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.Seed.Features;
using Infrastructure.Persistence.Seed.FormVersions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Persistence;

public static class ConfigurePersistenceServices
{
    // Azure Database for PostgreSQL flexible server's AAD/Entra token scope — fixed by Azure, not per-tenant.
    private const string PostgresManagedIdentityScope = "https://ossrdbms-aad.database.windows.net/.default";

    extension(IServiceCollection services)
    {
        public void AddPersistenceServices(IConfiguration configuration)
        {
            var applicationSettings = configuration.Get<ApplicationSettings>() ?? new ApplicationSettings();
            var databaseSettings = applicationSettings.Database ?? new DatabaseSettings();
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (databaseSettings.UseManagedIdentity)
            {
                // Npgsql refuses to register a password provider on a connection string that still carries a
                // Password segment — NpgsqlSlimDataSourceBuilder.PrepareConfiguration throws NotSupportedException
                // rather than ignoring it — so it has to be stripped here instead of relying on
                // ConnectionStrings:DefaultConnection being reformatted per environment.
                //
                // SslMode is forced to Require rather than left on Npgsql's default (Prefer), which silently
                // falls back to an unencrypted connection if the TLS handshake fails for any reason. Azure
                // Database for PostgreSQL flexible server's pg_hba.conf accepts only hostssl entries, so a
                // silent plaintext fallback surfaces as "no pg_hba.conf entry ... no encryption" rather than a
                // clear TLS error — and would otherwise risk sending the AAD access token as a cleartext
                // password.
                connectionString = new NpgsqlConnectionStringBuilder(connectionString)
                {
                    Password = null,
                    SslMode = SslMode.Require,
                }.ConnectionString;
            }

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();

            if (databaseSettings.UseManagedIdentity)
            {
                var credential = string.IsNullOrEmpty(databaseSettings.ManagedIdentityClientId)
                    ? new DefaultAzureCredential()
                    : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = databaseSettings.ManagedIdentityClientId });

                // Npgsql calls this on a timer rather than per-connection, and re-reads the connection string's
                // Username as the AAD principal name each time — the Password segment is never consulted once a
                // provider is registered. Refresh well inside the token's ~60-90 minute lifetime; a short failure
                // interval means a transient credential-endpoint hiccup is retried in seconds, not minutes.
                dataSourceBuilder.UsePeriodicPasswordProvider(
                    async (_, cancellationToken) =>
                    {
                        var token = await credential.GetTokenAsync(
                            new TokenRequestContext([PostgresManagedIdentityScope]),
                            cancellationToken);
                        return token.Token;
                    },
                    successRefreshInterval: TimeSpan.FromMinutes(4),
                    failureRefreshInterval: TimeSpan.FromSeconds(10));
            }

            var dataSource = dataSourceBuilder.Build();

            services.AddDbContext<ApplicationDbContext>(
                options => options
                    .UseNpgsql(
                        dataSource,
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

            services.AddDataProtectionServices(applicationSettings.AzureBlob, applicationSettings.Mfa);

            services.AddScoped<ApplicationDbContextInitialiser>();
            services.AddScoped<MaturityBaselineSeeder>();
            services.AddScoped<FormModuleVersionBackfill>();
            services.AddScoped<FormModuleVersionSync>();
            services.AddFeatureConvergences();
            services.AddRepositories();
            services.AddScoped<IFundingWindowResolver, FundingWindowResolver>();
            services.AddScoped<IBulkUploadFileParser, CsvParser>();
            services.AddScoped<IDataResetService, DataResetService>();
            services.AddScoped<ITransactionRunner, TransactionRunner>();
        }

        // Data Protection encrypts MFA TOTP secrets (MfaEncryptionService, purpose "MfaSecretProtection"). The
        // default key ring is per-process and in-memory: on the B1 App Service (always_on = false) the site
        // unloads after ~20 minutes idle, so the *next* cold start mints a fresh key ring that cannot decrypt
        // secrets encrypted under the old one — every verify for that user then throws CryptographicException,
        // surfaced as a generic 500 MFA_ERROR (VerifyMfaCommand's MfaUnknownException branch). Persisting the
        // key ring to blob storage keeps it stable across restarts and instances, matching the pattern already
        // proven in the sibling family-hubs-suite repo.
        //
        // Gated behind MfaSettings.UseBlobKeyPersistence (default false, same convention as
        // DatabaseSettings.UseManagedIdentity) rather than switching on AzureBlob:ConnectionString alone: an
        // environment can already have that connection string set for unrelated blob usage, and the first boot
        // that starts persisting keys is a one-time cutover that invalidates every already-enrolled secret — it
        // must happen deliberately, in the same release as clearing existing MFA data, not as a side effect of
        // config that was already there.
        private void AddDataProtectionServices(AzureBlobSettings? azureBlobSettings, MfaSettings mfaSettings)
        {
            var keysPersistedToBlob = false;
            if (mfaSettings.UseBlobKeyPersistence && !string.IsNullOrEmpty(azureBlobSettings?.ConnectionString))
            {
                try
                {
                    var containerClient = new BlobContainerClient(azureBlobSettings.ConnectionString, "data-protection-keys");
                    containerClient.CreateIfNotExists();

                    services.AddDataProtection()
                        .SetApplicationName("GamblingLevyPortal")
                        .PersistKeysToAzureBlobStorage(containerClient.GetBlobClient("keys.xml"));
                    keysPersistedToBlob = true;
                }
                catch (Exception ex)
                {
                    // Falls back to the ephemeral, per-instance key ring below rather than failing app startup
                    // outright — keeps local dev working without a configured storage account, at the cost of
                    // Data Protection payloads (MFA secrets) only being decryptable until the next restart.
                    Console.Error.WriteLine(
                        $"Data Protection: failed to reach blob storage for key persistence, falling back to an ephemeral key ring. {ex.Message}");
                }
            }

            if (!keysPersistedToBlob)
            {
                services.AddDataProtection().SetApplicationName("GamblingLevyPortal");
            }
        }

        // Every IFeatureConvergence in this assembly, found by type rather than listed: one that was written but never
        // registered would silently never run, leaving its flag on over data still in the off state.
        private void AddFeatureConvergences()
        {
            services.AddScoped<FeatureConvergenceRunner>();
            var convergences = typeof(IFeatureConvergence).Assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IFeatureConvergence).IsAssignableFrom(type));
            foreach (var convergence in convergences)
            {
                services.AddScoped(typeof(IFeatureConvergence), convergence);
            }
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

            services.AddScoped<MaturityAssessmentRepository>();
            services.AddScoped<IMaturityAssessmentRepository>(provider => provider.GetRequiredService<MaturityAssessmentRepository>());
            services.AddScoped<IMaturityAssessmentQueries>(provider => provider.GetRequiredService<MaturityAssessmentRepository>());
            services.AddScoped<IMaturityAssessmentAnswerRepository, MaturityAssessmentAnswerRepository>();
            services.AddScoped<IMaturityBaselineQueries, MaturityBaselineAnswerRepository>();

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
            services.AddScoped<CollectionReminderDispatchRepository>();
            services.AddScoped<ICollectionReminderDispatchRepository>(provider => provider.GetRequiredService<CollectionReminderDispatchRepository>());
            services.AddScoped<ICollectionReminderDispatchQueries>(provider => provider.GetRequiredService<CollectionReminderDispatchRepository>());

            services.AddScoped<FormModuleRepository>();
            services.AddScoped<IFormModuleRepository>(provider => provider.GetRequiredService<FormModuleRepository>());
            services.AddScoped<IFormModuleQueries>(provider => provider.GetRequiredService<FormModuleRepository>());

            services.AddScoped<FormModuleVersionRepository>();
            services.AddScoped<IFormModuleVersionRepository>(provider => provider.GetRequiredService<FormModuleVersionRepository>());
            services.AddScoped<IFormModuleVersionQueries>(provider => provider.GetRequiredService<FormModuleVersionRepository>());
        }
    }
}