using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Utilities.Utils;

[assembly: FunctionsStartup(typeof(ServiceBus.Processor.Function.Startup))]
namespace ServiceBus.Processor.Function
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var context = builder.GetContext();
            var config = new ConfigurationBuilder()
                                    .SetBasePath(context.ApplicationRootPath)
                                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                    .AddEnvironmentVariables()
                                    .Build();
            builder.Services.AddSingleton<IConfiguration>(config);

            // Bind configuration/environment variables
            builder.Services.AddSingleton<EnvironmentVariables>(provider =>
            {
                var config = provider.GetService<IConfiguration>();

                return new()
                {
                    ServiceBusTopicSubscription = config["ServiceBus:TopicSubscription"],
                    DatabricksInstance = config["Databricks:Instance"],
                    DatabricksAccessToken = config["Databricks:AccessToken"],
                    DatabricksWorkflowJobId_Ingest = config["Databricks:WorkflowJobId_Ingest"]
                };
            });

            // Register ServiceBus instances
            _ = bool.TryParse(config["ServiceBus:UseManagedIdentity"], out bool useManagedIdentity);
            builder.Services.AddServiceBusClientAndReceiver(useManagedIdentity,
                                                        config["ServiceBus:Topic"],
                                                        config["ServiceBus:TopicSubscription"],
                                                        config["ServiceBus:Name"],
                                                        config["ServiceBus:ConnectionString"]);
        }
    }
}
