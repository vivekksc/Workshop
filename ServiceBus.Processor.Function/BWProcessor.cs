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
    public class BWProcessor(
        IAzureClientFactory<ServiceBusClient> azSBClientFactory,
        IProcessorService processorService,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        private readonly ServiceBusClient _sbClient = azSBClientFactory.CreateClient(config.ServiceBusName);
        private readonly IProcessorService _processorService = processorService;

        [FunctionName("BWProcessor")]
        public async Task RunAsync([TimerTrigger("%BWProcessorRunScheduleExpression%")] TimerInfo myTimer,
                                   ExecutionContext funcContext,
                                   ILogger log)
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
                    sessionReceiver = await _sbClient.AcceptNextSessionAsync(_config.ServiceBusTopic_BW,
                                                                            _config.ServiceBusTopicSubscriptionBW,
                                                                            sessionReceiverOptions,
                                                                            cancellationTokenSource.Token);
                }
                catch (TaskCanceledException) { break; } // No sessions available

                if (sessionReceiver == null) break; // No more sessions available

                try
                {
                    sessionTasks.Add(_processorService.ProcessSessionAsync(sessionReceiver,
                                                                           log,
                                                                           _config.DatabricksWorkflowJobId_PublishBW,
                                                                           funcContext.FunctionName)); // Start processing in parallel
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
                log.LogInformation($"{funcContext.FunctionName} - {sessionTasks.Count} sessions processed.");
            }

            log.LogInformation($"{funcContext.FunctionName} - No sessions found.");
        }
    }
}
