using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Utilities.Contracts;
using Utilities.Services;
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
                    StorageAccountName = config["StorageAccount:Name"],
                    StorageAccount_UseManagedIdentity = Convert.ToBoolean(config["StorageAccount:UseManagedIdentity"]),
                    StorageAccount_ConnectionString = config["StorageAccount:ConnectionString"],
                    StorageAccountContainer_Ingestion = config["StorageAccount:Container_Ingestion"],
                    ServiceBusName = config["ServiceBus:Name"],
                    ServiceBusTopic_MDS = config["ServiceBus:Topic_MDS"],
                    ServiceBusTopic_BW = config["ServiceBus:Topic_BW"],
                    ServiceBusTopicSubscription_Update = config["ServiceBus:TopicSubscription_Update"],
                    ServiceBusTopicSubscription_Comment = config["ServiceBus:TopicSubscription_Comment"],
                    ServiceBusTopicSubscription_Kudos = config["ServiceBus:TopicSubscription_Kudos"],
                    ServiceBusTopicSubscription_Event = config["ServiceBus:TopicSubscription_Event"],
                    ServiceBusTopicSubscription_Bookmark = config["ServiceBus:TopicSubscription_Bookmark"],
                    ServiceBusTopicSubscription_Article = config["ServiceBus:TopicSubscription_Article"],
                    ServiceBusTopicSubscriptionBW = config["ServiceBus:TopicSubscription_BW"],
                    DatabricksInstance = config["Databricks:Instance"],
                    DatabricksAccessToken = config["Databricks:AccessToken"],
                    DatabricksWorkflowJobId_Ingest = config["Databricks:WorkflowJobId_Ingest"],
                    DatabricksWorkflowJobId_PublishBW = config["Databricks:WorkflowJobId_PublishBW"],
                    DatabricksWorkflowJobStatusPollingDelay_Seconds = Convert.ToInt16(config["Databricks:WorkflowJobStatusPollingDelay_Seconds"]),
                    DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest = Convert.ToInt16(config["Databricks:WorkflowJobStatusPollingMaxWait_Seconds_Ingest"]),
                    DatabricksWorkflowJobStatusPollingMaxWait_Seconds_PublishBW = Convert.ToInt16(config["Databricks:WorkflowJobStatusPollingMaxWait_Seconds_PublishBW"]),
                    MaxConcurrentSessions = Convert.ToInt16(config["MaxConcurrentSessions"]),
                    MaxMessagesPerSession = Convert.ToInt16(config["MaxMessagesPerSession"]),
                    MaxMessagesToProcessPerRun = Convert.ToInt16(config["MaxMessagesToProcessPerRun"]),
                    MaxConcurrentSessions_BW = Convert.ToInt16(config["MaxConcurrentSessions_BW"]),
                    MaxMessagesToProcessPerRun_BW = Convert.ToInt16(config["MaxMessagesToProcessPerRun_BW"]),
                    MaxWaitTimeForMessagesInMilliSeconds = Convert.ToInt16(config["MaxWaitTimeForMessagesInMilliSeconds"])
                };
            });

            // Register Application services
            builder.Services.AddScoped<IProcessorService, ProcessorService>();

            // Register ServiceBus instances
            _ = bool.TryParse(config["ServiceBus:UseManagedIdentity"], out bool useManagedIdentity);
            builder.Services.AddServiceBusClient(useManagedIdentity,
                                                config["ServiceBus:Name"],
                                                config["ServiceBus:ConnectionString"]);
        }
    }
}
