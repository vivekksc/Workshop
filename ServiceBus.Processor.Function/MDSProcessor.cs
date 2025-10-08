using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Contracts;
using Utilities.Utils;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace ServiceBus.Processor.Function
{
    public class MDSProcessor(
        IAzureClientFactory<ServiceBusClient> azSBClientFactory,
        //IHttpClientFactory httpClientFactory,
        IProcessorService processorService,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        private readonly ServiceBusClient _sbClient = azSBClientFactory.CreateClient(config.ServiceBusName);
        //private readonly HttpClient httpClient = httpClientFactory.CreateClient();
        private readonly IProcessorService _processorService = processorService;

        [Disable]
        [FunctionName("MDSProcessor-Update")]
        public async Task ProcessUpdatesAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Update,
                                  funcContext.FunctionName);
        }

        [Disable]
        [FunctionName("MDSProcessor-Comment")]
        public async Task ProcessCommentsAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Comment,
                                  funcContext.FunctionName);
        }

        [Disable]
        [FunctionName("MDSProcessor-Kudos")]
        public async Task ProcessKudosAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Kudos,
                                  funcContext.FunctionName);
        }

        [Disable]
        [FunctionName("MDSProcessor-Event")]
        public async Task ProcessEventAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Event,
                                  funcContext.FunctionName);
        }

        [Disable]
        [FunctionName("MDSProcessor-Bookmark")]
        public async Task ProcessBookmarkAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Bookmark,
                                  funcContext.FunctionName);
        }

        [Disable]
        [FunctionName("MDSProcessor-Article")]
        public async Task ProcessArticleAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
        {
            await ProcessMessages(log,
                                  _config.ServiceBusTopic_MDS,
                                  _config.ServiceBusTopicSubscription_Article,
                                  funcContext.FunctionName);
        }

        private async Task ProcessMessages(ILogger log, string topicName, string topicSubscriptionName, string processName)
        {
            List<Task> sessionTasks = [];

            // Accept sessions and process them concurrently
            for (int i = 0; i < _config.MaxConcurrentSessions; i++)
            {
                CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds));
                ServiceBusSessionReceiverOptions sessionReceiverOptions = new() { PrefetchCount = _config.MaxMessagesToProcessPerRun };
                ServiceBusSessionReceiver sessionReceiver;
                try
                {
                    sessionReceiver = await _sbClient.AcceptNextSessionAsync(topicName,
                                                                            topicSubscriptionName,
                                                                            sessionReceiverOptions,
                                                                            cancellationTokenSource.Token);
                }
                catch (TaskCanceledException) { break; } // No sessions available

                if (sessionReceiver == null) break; // No more sessions available

                try
                {
                    sessionTasks.Add(_processorService.ProcessSessionAsync(sessionReceiver,
                                                                           log,
                                                                           _config.DatabricksWorkflowJobId_Ingest,
                                                                           _config.DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest,
                                                                           processName)); // Start processing in parallel
                }
                catch
                {
                    if (!sessionReceiver.IsClosed)
                        await sessionReceiver.CloseAsync();
                }
            }

            if (sessionTasks.Count > 0)
            {
                await Task.WhenAll(sessionTasks); // Wait for all sessions to complete
                log.LogInformation($"{processName} - {sessionTasks.Count} sessions processed.");
            }

            log.LogInformation($"{processName} - No sessions found.");
        }
    }
}
