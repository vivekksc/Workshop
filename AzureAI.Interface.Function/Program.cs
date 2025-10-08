//using Azure.AI.TextAnalytics;
//using Azure.Identity;
//using Microsoft.Azure.Functions.Worker.Builder;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using System;


//var host = new HostBuilder()
//                .ConfigureFunctionsWorkerDefaults()
//                .ConfigureServices(services =>
//                {
//                    // Register the TextAnalyticsClient as a Singleton service.
//                    services.AddSingleton(provider =>
//                    {
//                        // Get the endpoint URL from environment variables (Application Settings).
//                        var endpointUri = Environment.GetEnvironmentVariable("TEXT_ANALYTICS_ENDPOINT");

//                        if (string.IsNullOrWhiteSpace(endpointUri))
//                        {
//                            throw new InvalidOperationException("The TEXT_ANALYTICS_ENDPOINT environment variable is not configured.");
//                        }

//                        // *** Managed Identity Authentication (DefaultAzureCredential) ***
//                        // DefaultAzureCredential is the key to using Managed Identity.
//                        // It checks for environment variables, then Visual Studio/Azure CLI login, and finally
//                        // the Managed Identity assigned to the Function App when running in Azure.
//                        Azure.Core.TokenCredential credential = new DefaultAzureCredential();

//                        // Instantiate the client with the endpoint and the credential.
//                        return new TextAnalyticsClient(new Uri(endpointUri), credential);
//                    });
//                })
//                .Build();

//host.Run();
