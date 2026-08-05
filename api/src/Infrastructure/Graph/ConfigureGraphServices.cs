using Application.Common.Settings;
using Application.Users.Interfaces;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;

namespace Infrastructure.Graph;
public static class ConfigureGraphServices
{
    extension(IServiceCollection services)
    {
        public void AddGraphServices()
        {
            services.SetupGraphService();
            services.AddScoped<UsersService>();
            services.AddScoped<IUsersService>(provider => provider.GetRequiredService<UsersService>());
        }

        private void SetupGraphService()
        {
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            var options = new TokenCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
            };

            services.AddScoped(provider =>
            {
                var settings = provider.GetRequiredService<ApplicationSettings>();
                var clientSecretCredential = new ClientSecretCredential(
                    settings.Graph.TenantId,
                    settings.Graph.ApplicationId,
                    settings.AzureAd.ClientSecret,
                    options);
                return new GraphServiceClient(clientSecretCredential, scopes);
            });
        }
    }
}