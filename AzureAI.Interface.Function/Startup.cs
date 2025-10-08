using Azure.AI.TextAnalytics;
using Azure.Identity;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

[assembly: FunctionsStartup(typeof(AzureAI.Interface.Function.Startup))]
namespace AzureAI.Interface.Function
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // Register the TextAnalyticsClient as a Singleton service.
            builder.Services.AddSingleton(provider =>
            {
                // Retrieve configuration to get the endpoint URL.
                var config = provider.GetRequiredService<IConfiguration>();
                var endpointUri = config["TEXT_ANALYTICS_ENDPOINT"];

                if (string.IsNullOrEmpty(endpointUri))
                {
                    // Log or handle missing configuration
                    throw new InvalidOperationException("The TEXT_ANALYTICS_ENDPOINT setting is not configured.");
                }

                // *** Managed Identity Authentication (DefaultAzureCredential) ***
                // DefaultAzureCredential automatically uses the Function App's Managed Identity when deployed.
                Azure.Core.TokenCredential credential = new DefaultAzureCredential();

                // Instantiate the client with the endpoint and the credential.
                return new TextAnalyticsClient(new Uri(endpointUri), credential);
            });
        }
    }
}
