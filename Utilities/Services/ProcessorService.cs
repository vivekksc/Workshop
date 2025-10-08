using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Utilities.Contracts;
using Utilities.Utils;

namespace Utilities.Services
{
    public class ProcessorService(
        IHttpClientFactory httpClientFactory,
        EnvironmentVariables config) : IProcessorService
    {
        private readonly EnvironmentVariables _config = config;
        private readonly HttpClient httpClient = httpClientFactory.CreateClient();

        public async Task ProcessSessionAsync(ServiceBusSessionReceiver sessionReceiver,
                                              ILogger logger,
                                              string databricksJobId,
                                              int databricksJobStatusPollingMaxWaitSeconds,
                                              string processorName)
        {
            logger.LogInformation($"{processorName} - Processing session: {sessionReceiver.SessionId}");
            while (true)
            {
                // Fetch messages in order within the session
                IReadOnlyList<ServiceBusReceivedMessage> messages =
                    await sessionReceiver.ReceiveMessagesAsync(maxMessages: _config.MaxMessagesPerSession, TimeSpan.FromMilliseconds(_config.MaxWaitTimeForMessagesInMilliSeconds));

                if (messages.Count == 0) break; // No more messages in session

                foreach (var message in messages)
                {
                    logger.LogInformation($"{processorName} - Processing message {message.MessageId} in Session {sessionReceiver.SessionId}");

                    try
                    {
                        await ProcessMessageAsync(message, logger, databricksJobId, databricksJobStatusPollingMaxWaitSeconds, processorName);
                        await sessionReceiver.CompleteMessageAsync(message); // Ensure ordered completion
                    }
                    catch
                    {
                        await sessionReceiver.AbandonMessageAsync(message); // Abandon message to attempt further delivery if configured in SB subscription.
                    }
                }
            }

            await sessionReceiver.CloseAsync(); // Close session receiver after processing
        }

        public async Task ProcessMessageAsync(ServiceBusReceivedMessage message,
                                              ILogger logger,
                                              string databricksJobId,
                                              int databricksJobStatusPollingMaxWaitSeconds,
                                              string processorName)
        {
            using MemoryStream eventPayloadStream = new(message.Body.ToArray());
            string eventEntity = message.Subject;
            string eventId = message.SessionId;
            string logDetail = $"Entity: {eventEntity}, EntityId: {eventId}";

            try
            {
                string blobName = $"{eventId}_{message.MessageId}_{processorName}.json";
                var uploadResponse = await UploadBlobAsync(eventPayloadStream, blobName);
                logger.LogInformation($"{processorName} - {logDetail}, PayloadBlobUploadStatus: {uploadResponse.ReasonPhrase}");

                var payload = new
                {
                    job_id = databricksJobId,
                    job_parameters = new
                    {
                        entity = eventEntity,
                        payloadFileAbsolutePath = uploadResponse.BlobAbsolutePath
                    }
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

                HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);
                string responseContent = await response.Content.ReadAsStringAsync();
                logDetail = $"{processorName} - {logDetail}, IngestionResponse: {responseContent}";
                logger.LogInformation(logDetail);
                response.EnsureSuccessStatusCode();

                JsonDocument responseJson = JsonDocument.Parse(responseContent);
                var isRunIdFound = responseJson.RootElement.GetProperty("run_id").TryGetInt64(out long dbxJobRunId);
                if (isRunIdFound)
                    await WaitForJobCompletionAsync(logger, dbxJobRunId, databricksJobStatusPollingMaxWaitSeconds, processorName);
                else
                    await Task.Delay(TimeSpan.FromSeconds(databricksJobStatusPollingMaxWaitSeconds));

                //var deleteResponse = await DeleteBlobAsync(blobName);
                //logger.LogInformation($"{processorName} - {logDetail}, PayloadBlobDeleteStatus: {deleteResponse.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                string exceptionDetails = $"{processorName} - {logDetail} | ExceptionMessage: {ex.Message} | InnerException: {ex.InnerException} | StackTrace: {ex.StackTrace}";
                logger.LogError(exceptionDetails);
                throw new WebException(exceptionDetails);
            }
        }

        public async Task<bool> WaitForJobCompletionAsync(ILogger logger,
                                                          long jobRunId,
                                                          int jobStatusPollingMaxWaitSeconds,
                                                          string processorName)
        {
            string url = $"https://{_config.DatabricksInstance}/api/2.1/jobs/runs/get?run_id={jobRunId}";
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

            DateTime startTime = DateTime.Now;
            TimeSpan maxWaitTime = TimeSpan.FromSeconds(jobStatusPollingMaxWaitSeconds);

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode(); // Throw exception if not 2XX

                    string responseBody = await response.Content.ReadAsStringAsync();
                    logger.LogInformation(responseBody);
                    JsonDocument json = JsonDocument.Parse(responseBody);
                    string jobStatus = json.RootElement.GetProperty("state")
                                          .GetProperty("life_cycle_state")
                                          .GetString();


                    if (jobStatus == "TERMINATED" || jobStatus == "INTERNAL_ERROR")
                    {
                        throw new Exception($"{processorName} - Job {jobRunId} {jobStatus}. Details - {responseBody}");
                    }
                    else if (jobStatus == "SUCCESS")
                    {
                        logger.LogInformation($"{processorName} - Job {jobRunId} completed.");
                        return true;
                    }
                    else
                    {
                        logger.LogInformation($"{processorName} - Waiting for job {jobRunId} (status: {jobStatus}) to finish...");
                        await Task.Delay(TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds));
                    }
                }
                catch (Exception ex)
                {
                    throw;
                    //logger.LogError($"{processorName} - Error checking job status: {ex.Message}");
                    //await Task.Delay(TimeSpan.FromSeconds(_config.DatabricksWorkflowJobStatusPollingDelay_Seconds)); // Wait before retrying
                }
            }

            Console.WriteLine($"{processorName} - Timeout reached! Proceeding even though job {jobRunId} is still running.");
            return true;  // Force return `true` after timeout
        }


        private async Task<BlobUploadResponse> UploadBlobAsync(MemoryStream blobContent, string blobName)
        {
            var blobEndpoint = new Uri($"https://{_config.StorageAccountName}.blob.core.windows.net/{_config.StorageAccountContainer_Ingestion}");
            BlobContainerClient containerClient;

            if (_config.StorageAccount_UseManagedIdentity)
            {
                var credential = new DefaultAzureCredential();
                containerClient = new BlobContainerClient(blobEndpoint, credential);
            }
            else
            {
                containerClient = new BlobContainerClient(_config.StorageAccount_ConnectionString, _config.StorageAccountContainer_Ingestion);
            }

            await containerClient.CreateIfNotExistsAsync();
            var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

            var result = await blockBlobClient.UploadAsync(blobContent);
            var response = result.GetRawResponse();

            string uploadResponse = default;
            var responseStream = response?.ContentStream;
            if (responseStream != null)
            {
                responseStream.Seek(0, SeekOrigin.Begin);
                using StreamReader reader = new(responseStream);
                uploadResponse = reader.ReadToEndAsync().Result;
            }

            return new BlobUploadResponse
            {
                IsSuccess = !response.IsError,
                Status = response.Status,
                ReasonPhrase = response.ReasonPhrase,
                Content = uploadResponse,
                BlobURI = blockBlobClient.Uri.AbsoluteUri,
                BlobAbsolutePath = blockBlobClient.Uri.AbsolutePath
            };
        }

        private async Task<BlobUploadResponse> DeleteBlobAsync(string blobName)
        {
            var blobEndpoint = new Uri($"https://{_config.StorageAccountName}.blob.core.windows.net/{_config.StorageAccountContainer_Ingestion}");
            BlobContainerClient containerClient;

            if (_config.StorageAccount_UseManagedIdentity)
            {
                var credential = new DefaultAzureCredential();
                containerClient = new BlobContainerClient(blobEndpoint, credential);
            }
            else
            {
                containerClient = new BlobContainerClient(_config.StorageAccount_ConnectionString, _config.StorageAccountContainer_Ingestion);
            }

            await containerClient.CreateIfNotExistsAsync();
            var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

            var result = await blockBlobClient.DeleteIfExistsAsync();
            var response = result.GetRawResponse();

            string deleteResponse = default;
            var responseStream = response?.ContentStream;
            if (responseStream != null)
            {
                responseStream.Seek(0, SeekOrigin.Begin);
                using StreamReader reader = new(responseStream);
                deleteResponse = reader.ReadToEndAsync().Result;
            }

            return new BlobUploadResponse
            {
                IsSuccess = !response.IsError,
                Status = response.Status,
                ReasonPhrase = response.ReasonPhrase,
                Content = deleteResponse,
                BlobURI = blockBlobClient.Uri.AbsoluteUri,
                BlobAbsolutePath = blockBlobClient.Uri.AbsolutePath
            };
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
    }

    public sealed class BlobUploadResponse()
    {
        public bool IsSuccess { get; set; }
        public int Status { get; set; }
        public string ReasonPhrase { get; set; }
        public string Content { get; set; }
        public string BlobURI { get; set; }
        public string BlobAbsolutePath { get; set; }

    }
}
