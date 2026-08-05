using Application.Common.Interfaces;
using Application.Common.Settings;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Blob;

public static class ConfigureBlobService
{
    extension(IServiceCollection services)
    {
        public void AddBlobServices()
        {
            services.SetupBlobService();
            services.AddScoped<BlobService>();
            services.AddScoped<IBlobService>(provider => provider.GetRequiredService<BlobService>());
        }

        private void SetupBlobService()
        {
            services.AddScoped(provider =>
            {
                var settings = provider.GetRequiredService<ApplicationSettings>();

                return new BlobServiceClient(settings.AzureBlob.ConnectionString);
            });
        }
    }
}