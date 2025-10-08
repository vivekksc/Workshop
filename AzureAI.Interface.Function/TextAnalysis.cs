using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzureAI.Interface.Function
{
    // Simple class to deserialize the incoming JSON request body.
    public class TextRequest
    {
        // The input string provided in the HTTP request body.
        public string? Text { get; set; }
    }

    public class TextAnalysis(TextAnalyticsClient textAnalyticsClient, ILogger<TextAnalysis> log)
    {
        private readonly ILogger<TextAnalysis> _logger = log;
        private readonly TextAnalyticsClient _textAnalyticsClient = textAnalyticsClient;

        [FunctionName(nameof(AnalyzeSentiment))]
        public async Task<IActionResult> AnalyzeSentiment(
            [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethod.Post), Route = "sentiment")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request for Sentiment Analysis (In-Process).");

            TextRequest? requestData;
            try
            {
                // Read and deserialize the request body from the HttpRequest stream
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                // Use case-insensitive deserialization for robustness
                requestData = JsonSerializer.Deserialize<TextRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to deserialize request body.");
                return new BadRequestObjectResult(new { error = "Invalid request body format or missing 'text' property." });
            }

            if (string.IsNullOrWhiteSpace(requestData?.Text))
            {
                return new BadRequestObjectResult(new { error = "Please pass a 'text' property in the request body." });
            }

            try
            {
                // 1. Invoke Azure AI Language Service for sentiment analysis
                DocumentSentiment result = await _textAnalyticsClient.AnalyzeSentimentAsync(requestData.Text);

                // 2. Return the structured response as-is (IActionResult handles serialization)
                var responseContent = new
                {
                    Input = requestData.Text,
                    Sentiment = result.Sentiment.ToString(),
                    result.ConfidenceScores,
                    SentenceSentiments = result.Sentences.Select(s => new {
                        s.Text,
                        Sentiment = s.Sentiment.ToString(),
                        s.ConfidenceScores
                    })
                };

                return new OkObjectResult(responseContent);
            }
            catch (RequestFailedException ex)
            {
                log.LogError(ex, "Azure AI Language Service request failed for Sentiment Analysis.");
                return new ObjectResult(new { error = $"AI Service Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An unexpected error occurred during Sentiment Analysis.");
                return new ObjectResult(new { error = $"Internal Server Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            }
        }

        /// <summary>
        /// HTTP Trigger for performing Language Detection on an input string.
        /// Route: /language
        /// </summary>
        [FunctionName(nameof(DetectLanguage))]
        public async Task<IActionResult> DetectLanguage(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "language")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request for Language Detection (In-Process).");

            TextRequest? requestData;
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                requestData = JsonSerializer.Deserialize<TextRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to deserialize request body.");
                return new BadRequestObjectResult(new { error = "Invalid request body format or missing 'text' property." });
            }

            if (string.IsNullOrWhiteSpace(requestData?.Text))
            {
                return new BadRequestObjectResult(new { error = "Please pass a 'text' property in the request body." });
            }

            try
            {
                // 1. Invoke Azure AI Language Service for language detection
                Response<DetectedLanguage> response = await _textAnalyticsClient.DetectLanguageAsync(requestData.Text);
                DetectedLanguage result = response.Value;

                // 2. Return the structured response as-is
                var responseContent = new
                {
                    Input = requestData.Text,
                    DetectedLanguage = new
                    {
                        result.Name,
                        result.Iso6391Name,
                        result.ConfidenceScore
                    }
                };
                return new OkObjectResult(responseContent);
            }
            catch (RequestFailedException ex)
            {
                log.LogError(ex, "Azure AI Language Service request failed for Language Detection.");
                return new ObjectResult(new { error = $"AI Service Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An unexpected error occurred during Language Detection.");
                return new ObjectResult(new { error = $"Internal Server Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            }
        }
    }
}

