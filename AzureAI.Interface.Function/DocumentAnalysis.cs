using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.AI.Translation.Document;
using Azure.Identity;
using Azure;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace AzureAI.Interface.Function
{
    // // Simple class to deserialize the incoming JSON request body.
    // public class TextRequest
    // {
    //     // The input string provided in the HTTP request body.
    //     public string? Text { get; set; }
    // }
    public record TranslationRequest(string SourceUrl, string TargetContainerUrl, string TargetLanguageCode);

    public class DocumentAnalysis(DocumentTranslationClient translationClient
                                , ILogger<DocumentAnalysis> log
                                , IConfiguration configuration)
    {
        private readonly ILogger<DocumentAnalysis> _logger = log;
        private readonly DocumentTranslationClient _translationClient = new DocumentTranslationClient(new Uri(endpoint), new DefaultAzureCredential());
        string endpoint = configuration["DocumentTranslationEndpoint"] ?? throw new ArgumentNullException("DocumentTranslationEndpoint");

        [FunctionName(nameof(TranslateDocument))]
        public async Task<IActionResult> TranslateDocument(
            [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethod.Post), Route = "TranslateDocument")] HttpRequest req,
            ILogger log)
        {
            _logger.LogInformation("HTTP trigger function started processing document translation request.");

            TranslationRequest requestBody;
            try
            {
                // 1. Deserialize the request body
                string requestJson = await new StreamReader(req.Body).ReadToEndAsync();
                requestBody = JsonSerializer.Deserialize<TranslationRequest>(requestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

                if (string.IsNullOrEmpty(requestBody.SourceUrl) || 
                    string.IsNullOrEmpty(requestBody.TargetContainerUrl) || 
                    string.IsNullOrEmpty(requestBody.TargetLanguageCode))
                {
                    return req.CreateResponse(HttpStatusCode.BadRequest, "Missing required fields: SourceUrl, TargetContainerUrl, or TargetLanguageCode.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse request body.");
                return req.CreateResponse(HttpStatusCode.BadRequest, $"Invalid request body format: {ex.Message}");
            }
            
            // Validate URIs
            if (!Uri.TryCreate(requestBody.SourceUrl, UriKind.Absolute, out Uri? sourceBlobUri) ||
                !Uri.TryCreate(requestBody.TargetContainerUrl, UriKind.Absolute, out Uri? targetContainerUri))
            {
                 return req.CreateResponse(HttpStatusCode.BadRequest, "SourceUrl or TargetContainerUrl is not a valid absolute URI.");
            }

            try
            {
                _logger.LogInformation($"Source URI: {sourceBlobUri}");
                _logger.LogInformation($"Target Container URI: {targetContainerUri}");

                // 2. Define the translation inputs using the direct URIs from the request
                var source = new DocumentTranslationSource(sourceBlobUri);

                // Target URL must be the container URI
                var target = new DocumentTranslationTarget(targetContainerUri, requestBody.TargetLanguageCode);

                var input = new DocumentTranslationInput(source, new[] { target });

                // 3. Start the asynchronous batch translation
                _logger.LogInformation($"Starting translation job (Language: {requestBody.TargetLanguageCode}) via Managed Identity.");

                // Note: The Document Translation service MUST have the Function App's Managed Identity 
                // granted access to the source and target storage accounts.
                DocumentTranslationOperation operation = await _translationClient.StartTranslationAsync(input);

                // --- NEW LOGIC: Wait for job completion and retrieve results ---
                _logger.LogInformation($"Translation job submitted. Waiting for completion. Operation ID: {operation.Id}");

                // Wait for the job to complete. This is the main blocking call.
                await operation.WaitForCompletionAsync();

                _logger.LogInformation($"Translation Job Status: {operation.Status}");
                _logger.LogInformation($"Total Documents: {operation.Details.DocumentsTotal}, Documents Failed: {operation.Details.DocumentsFailed}");

                // Iterate over document status to check for failures and log details
                await foreach (var documentStatus in operation.GetDocumentStatusesAsync())
                {
                    _logger.LogInformation($"Document: {documentStatus.SourceDocumentUri.Segments.Last()}, Status: {documentStatus.Status}, Translated To: {documentStatus.TranslatedToLanguageCode}");

                    if (documentStatus.Status == DocumentTranslationStatus.Failed)
                    {
                        _logger.LogError($"Translation failed for source document: {documentStatus.SourceDocumentUri}. Error: {documentStatus.Error.Message}");
                    }
                }

                // 4. Return a final response with the completed job status
                // Use HTTP 200 OK since the function waited and successfully returned the final result.
                var finalStatus = new
                {
                    OperationId = operation.Id,
                    Status = operation.Status.ToString(),
                    TotalDocuments = operation.Details.DocumentsTotal,
                    SuccessfulDocuments = operation.Details.DocumentsTotal - operation.Details.DocumentsFailed,
                    FailedDocuments = operation.Details.DocumentsFailed,
                    Message = operation.Status == DocumentTranslationStatus.Succeeded ? "Translation completed successfully." : "Translation failed or completed with errors."
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(finalStatus);
                _logger.LogInformation($"Translation job finished. Operation ID: {operation.Id}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while starting translation: {ex.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError, $"Translation service error: {ex.Message}");
            }
            
            // log.LogInformation("C# HTTP trigger function processed a request for Document Translation (In-Process).");

            // TextRequest? requestData;
            // try
            // {
            //     // Read and deserialize the request body from the HttpRequest stream
            //     string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            //     // Use case-insensitive deserialization for robustness
            //     requestData = JsonSerializer.Deserialize<TextRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            // }
            // catch (Exception ex)
            // {
            //     log.LogError(ex, "Failed to deserialize request body.");
            //     return new BadRequestObjectResult(new { error = "Invalid request body format or missing 'text' property." });
            // }

            // if (string.IsNullOrWhiteSpace(requestData?.Text))
            // {
            //     return new BadRequestObjectResult(new { error = "Please pass a 'text' property in the request body." });
            // }

            // try
            // {
            //     // 1. Invoke Azure AI Language Service for sentiment analysis
            //     DocumentSentiment result = await _textAnalyticsClient.AnalyzeSentimentAsync(requestData.Text);

            //     // 2. Return the structured response as-is (IActionResult handles serialization)
            //     var responseContent = new
            //     {
            //         Input = requestData.Text,
            //         Sentiment = result.Sentiment.ToString(),
            //         result.ConfidenceScores,
            //         SentenceSentiments = result.Sentences.Select(s => new {
            //             s.Text,
            //             Sentiment = s.Sentiment.ToString(),
            //             s.ConfidenceScores
            //         })
            //     };

            //     return new OkObjectResult(responseContent);
            // }
            // catch (RequestFailedException ex)
            // {
            //     log.LogError(ex, "Azure AI Language Service request failed for Sentiment Analysis.");
            //     return new ObjectResult(new { error = $"AI Service Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            // }
            // catch (Exception ex)
            // {
            //     log.LogError(ex, "An unexpected error occurred during Sentiment Analysis.");
            //     return new ObjectResult(new { error = $"Internal Server Error: {ex.Message}" }) { StatusCode = (int)HttpStatusCode.InternalServerError };
            // }
        }
    }
}
