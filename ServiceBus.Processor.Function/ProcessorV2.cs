using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Utilities.Utils;

namespace ServiceBus.Processor.Function
{
    public class ProcessorV2(
        IAzureClientFactory<ServiceBusClient> azSBClientFactory,
        IHttpClientFactory httpClientFactory,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        private readonly ServiceBusClient _sbClient = azSBClientFactory.CreateClient(config.ServiceBusName);
        private readonly HttpClient httpClient = httpClientFactory.CreateClient();

        [FunctionName("ScheduledProcessorV2")]
        public async Task RunAsync([TimerTrigger("%ProcessorRunScheduleExpression%")] TimerInfo myTimer, ILogger log)
        {
            List<Task> sessionTasks = [];

            // Accept sessions and process them concurrently
            for (int i = 0; i < _config.MaxConcurrentSessions; i++)
            {
                ServiceBusSessionReceiver sessionReceiver = await _sbClient.AcceptNextSessionAsync(_config.ServiceBusTopic, _config.ServiceBusTopicSubscription);
                if (sessionReceiver == null) break; // No more sessions available

                sessionTasks.Add(ProcessSessionAsync(sessionReceiver, log)); // Start processing in parallel
            }

            await Task.WhenAll(sessionTasks); // Wait for all sessions to complete
            log.LogInformation("All sessions processed.");
        }

        private async Task ProcessSessionAsync(ServiceBusSessionReceiver sessionReceiver, ILogger log)
        {
            log.LogInformation($"Processing session: {sessionReceiver.SessionId}");
            while (true)
            {
                // Fetch messages in order within the session
                IReadOnlyList<ServiceBusReceivedMessage> messages =
                    await sessionReceiver.ReceiveMessagesAsync(maxMessages: _config.MaxMessagesPerSession, TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds));

                if (messages.Count == 0) break; // No more messages in session

                foreach (var message in messages)
                {
                    Console.WriteLine($"Processing message {message.MessageId} in Session {sessionReceiver.SessionId}");

                    await ProcessMessageAsync(message, log);

                    await sessionReceiver.CompleteMessageAsync(message); // Ensure ordered completion
                }
            }

            await sessionReceiver.CloseAsync(); // Close session receiver after processing
        }

        private async Task ProcessMessageAsync(ServiceBusReceivedMessage message, ILogger log)
        {
            string eventPayload = Encoding.UTF8.GetString(message.Body.ToArray());
            string eventEntity = message.Subject;
            string eventId = message.SessionId;
            string logDetail = $"Entity: {eventEntity}, EntityId: {eventId}";

            try
            {
                var payload = new
                {
                    job_id = _config.DatabricksWorkflowJobId_Ingest,
                    job_parameters = new
                    {
                        entity = eventEntity,
                        payload = CompressAndBase64Encode(eventPayload)
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

                HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);
                string responseContent = await response.Content.ReadAsStringAsync();
                logDetail = $"{logDetail}, IngestionResponse: {responseContent}";
                log.LogInformation(logDetail);

                JsonDocument responseJson = JsonDocument.Parse(responseContent);
                int dbxJobRunId = responseJson.RootElement.GetProperty("run_id").GetInt32();
                await WaitForJobCompletionAsync(log, dbxJobRunId);
            }
            catch (Exception ex)
            {
                string excpetionDetails = $"{logDetail} | ExceptionMessage: {ex.Message} | InnerException: {ex.InnerException} | StackTrace: {ex.StackTrace}";
                log.LogError(excpetionDetails);
                throw;
            }
        }

        private static string CompressAndBase64Encode(string jsonString)
        {
            // Convert the JSON string to bytes
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            // Compress the bytes using Gzip
            using (var outputStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                {
                    gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
                }

                // Get the compressed bytes
                byte[] compressedBytes = outputStream.ToArray();

                // Encode the compressed bytes to base64
                string base64String = Convert.ToBase64String(compressedBytes);
                return base64String;
            }
        }

        public async Task<bool> WaitForJobCompletionAsync(ILogger log, int jobRunId)
        {
            string url = $"https://{_config.DatabricksInstance}/api/2.1/jobs/runs/get?run_id={jobRunId}";
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

            DateTime startTime = DateTime.Now;
            TimeSpan maxWaitTime = TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingMaxWait_Seconds);

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode(); // Throw exception if not 2XX

                    string responseBody = await response.Content.ReadAsStringAsync();
                    log.LogInformation(responseBody);
                    JsonDocument json = JsonDocument.Parse(responseBody);
                    string jobStatus = json.RootElement.GetProperty("state")
                                          .GetProperty("life_cycle_state")
                                          .GetString();

                    if (jobStatus == "TERMINATED" || jobStatus == "SUCCESS")
                    {
                        log.LogInformation($"Job {jobRunId} completed.");
                        return true;
                    }
                    else
                    {
                        log.LogInformation($"Waiting for job {jobRunId} (status: {jobStatus}) to finish...");
                        await Task.Delay(TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking job status: {ex.Message}");
                    await Task.Delay(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds); // Wait before retrying
                }
            }

            Console.WriteLine($"Timeout reached! Proceeding even though job {jobRunId} is still running.");
            return true;  // Force return `true` after timeout
        }
    }
}
