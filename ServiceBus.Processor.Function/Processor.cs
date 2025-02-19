using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Azure;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Utilities.Utils;

namespace ServiceBus.Processor.Function
{
    public class Processor(
        IAzureClientFactory<ServiceBusReceiver> azClientFactory,
        IHttpClientFactory httpClientFactory,
        EnvironmentVariables config
        )
    {
        private readonly EnvironmentVariables _config = config;
        private readonly ServiceBusReceiver _receiver = azClientFactory.CreateClient(config.ServiceBusTopicSubscription);
        private readonly HttpClient httpClient = httpClientFactory.CreateClient();
        //private readonly HttpClient httpClient = new();

        [FunctionName("Processor")]
        public async Task RunAsync([TimerTrigger("*/5 * * * * *")] TimerInfo myTimer)
        {
            var messages = await _receiver.ReceiveMessagesAsync(20);
            foreach (var message in messages)
            {
                try
                {
                    // Process the message
                    string eventPayload = Encoding.UTF8.GetString(message.Body.ToArray());

                    var payload = new
                    {
                        job_id = _config.DatabricksWorkflowJobId_Ingest,
                        job_parameters = new
                        {
                            payload = CompressAndBase64Encode(eventPayload)
                        }
                    };

                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.DatabricksAccessToken);

                    HttpResponseMessage response = await httpClient.PostAsync($"https://{_config.DatabricksInstance}/api/2.1/jobs/run-now", content);
                }
                catch (Exception ex)
                {

                }
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
    }
}
