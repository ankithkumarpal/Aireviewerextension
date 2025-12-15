using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AiReviewer.Functions.Models;

namespace AiReviewer.Functions.Services;

/// <summary>
/// Azure Functions for Team Learning API
/// 
/// Endpoints:
///   POST /api/feedback  - Submit new feedback
///   GET  /api/patterns  - Get learned patterns for a file extension
///   GET  /api/stats     - Get overall learning statistics
///   GET  /api/health    - Health check endpoint
/// </summary>
public class TeamLearningFunctions
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TeamLearningFunctions> _logger;
    private const string TableName = "Feedback";

    public TeamLearningFunctions(
        TableServiceClient tableServiceClient,
        IConfiguration configuration,
        ILogger<TeamLearningFunctions> logger)
    {
        _tableServiceClient = tableServiceClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Validates the API key from the request header
    /// </summary>
    private bool ValidateApiKey(HttpRequestData request)
    {
        var expectedKey = _configuration["ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            _logger.LogWarning("ApiKey not configured in application settings");
            return true; // Allow if not configured (dev mode)
        }

        if (request.Headers.TryGetValues("X-Api-Key", out var values))
        {
            return values.FirstOrDefault() == expectedKey;
        }

        return false;
    }

    /// <summary>
    /// Creates an unauthorized response
    /// </summary>
    private async Task<HttpResponseData> CreateUnauthorizedResponse(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Unauthorized);
        await response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
        return response;
    }

    // POST /api/feedback - Submit new feedback

    /// <summary>
    /// Submit feedback for a code review suggestion.
    /// 
    /// Request Body (JSON):
    /// {
    ///   "fileExtension": ".cs",
    ///   "rule": "STYLE",
    ///   "codeSnippet": "if(x==1)",
    ///   "suggestion": "Add spaces around ==",
    ///   "issueHash": "abc123",
    ///   "isHelpful": false,
    ///   "reason": "Not relevant to our codebase",
    ///   "correction": null,
    ///   "contributor": "john.doe",
    ///   "repository": "MyProject"
    /// }
    /// 
    /// Response: 201 Created with the created entity ID
    /// </summary>
    [Function("SubmitFeedback")]
    public async Task<HttpResponseData> SubmitFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "feedback")] HttpRequestData request)
    {
        _logger.LogInformation("Received feedback submission request");

        // Validate API key
        if (!ValidateApiKey(request))
        {
            return await CreateUnauthorizedResponse(request);
        }

        try
        {
            // Parse request body
            var requestBody = await request.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                var badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request body is empty" });
                return badRequest;
            }

            var feedbackRequest = JsonSerializer.Deserialize<FeedbackRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (feedbackRequest == null)
            {
                var badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            // Create table if it doesn't exist
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            // Create entity
            var entity = new FeedbackEntity
            {
                PartitionKey = feedbackRequest.FileExtension.ToLowerInvariant(),
                RowKey = Guid.NewGuid().ToString(),
                Rule = feedbackRequest.Rule,
                CodeSnippet = TruncateIfNeeded(feedbackRequest.CodeSnippet, 30000),
                Suggestion = TruncateIfNeeded(feedbackRequest.Suggestion, 30000),
                IssueHash = feedbackRequest.IssueHash,
                IsHelpful = feedbackRequest.IsHelpful,
                Reason = feedbackRequest.Reason,
                Correction = feedbackRequest.Correction,
                Contributor = feedbackRequest.Contributor,
                Repository = feedbackRequest.Repository,
                FeedbackDate = DateTime.UtcNow
            };

            // Insert into table (atomic, no concurrency issues!)
            await tableClient.AddEntityAsync(entity);

            _logger.LogInformation("Feedback submitted: {RowKey} from {Contributor}", 
                entity.RowKey, entity.Contributor);

            // Return success
            var response = request.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                id = entity.RowKey,
                message = "Feedback recorded successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting feedback");
            var errorResponse = request.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to submit feedback" });
            return errorResponse;
        }
    }

    // GET /api/patterns - Get learned patterns for a file extension

    /// <summary>
    /// Get learned patterns for a specific file extension.
    /// 
    /// Query Parameters:
    ///   ext (required): File extension, e.g., ".cs"
    ///   minOccurrences (optional): Minimum occurrences to include (default: 2)
    ///   maxResults (optional): Maximum patterns to return (default: 15)
    ///   minAccuracy (optional): Minimum accuracy percentage (default: 0)
    /// 
    /// Response: PatternsResponse with list of patterns
    /// </summary>
    [Function("GetPatterns")]
    public async Task<HttpResponseData> GetPatterns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "patterns")] HttpRequestData request)
    {
        // Validate API key
        if (!ValidateApiKey(request))
        {
            return await CreateUnauthorizedResponse(request);
        }

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
            var ext = query["ext"]?.ToLowerInvariant();
            var minOccurrences = int.TryParse(query["minOccurrences"], out var mo) ? mo : 2;
            var maxResults = int.TryParse(query["maxResults"], out var mr) ? mr : 15;
            var minAccuracy = double.TryParse(query["minAccuracy"], out var ma) ? ma : 0;

            if (string.IsNullOrEmpty(ext))
            {
                var badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Query parameter 'ext' is required" });
                return badRequest;
            }

            _logger.LogInformation("Getting patterns for extension: {Extension}", ext);

            // Query table by partition key (file extension)
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            
            // Check if table exists
            try
            {
                await tableClient.CreateIfNotExistsAsync();
            }
            catch { /* Ignore if exists */ }

            var feedbackItems = new List<FeedbackEntity>();
            await foreach (var entity in tableClient.QueryAsync<FeedbackEntity>(
                filter: $"PartitionKey eq '{ext}'"))
            {
                feedbackItems.Add(entity);
            }

            // Group by pattern (Rule + IssueHash)
            var patterns = feedbackItems
                .GroupBy(f => $"{f.Rule}|{f.PartitionKey}|{f.IssueHash}")
                .Where(g => g.Count() >= minOccurrences)
                .Select(g =>
                {
                    var helpful = g.Count(f => f.IsHelpful);
                    var notHelpful = g.Count(f => !f.IsHelpful);
                    var total = g.Count();
                    var accuracy = total > 0 ? (double)helpful / total * 100 : 0;

                    return new LearnedPatternDto
                    {
                        PatternKey = g.Key,
                        Rule = g.First().Rule,
                        FileExtension = ext,
                        TotalOccurrences = total,
                        HelpfulCount = helpful,
                        NotHelpfulCount = notHelpful,
                        Accuracy = Math.Round(accuracy, 1),
                        Examples = g.Take(3).Select(f => new FewShotExampleDto
                        {
                            CodeSnippet = TruncateIfNeeded(f.CodeSnippet, 500),
                            OriginalSuggestion = f.Suggestion,
                            WasHelpful = f.IsHelpful,
                            Correction = f.Correction,
                            Reason = f.Reason
                        }).ToList()
                    };
                })
                .Where(p => p.Accuracy >= minAccuracy)
                .OrderByDescending(p => p.Accuracy)
                .ThenByDescending(p => p.TotalOccurrences)
                .Take(maxResults)
                .ToList();

            var result = new PatternsResponse
            {
                Patterns = patterns,
                TotalFeedbackCount = feedbackItems.Count
            };

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting patterns");
            var errorResponse = request.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get patterns" });
            return errorResponse;
        }
    }

    // GET /api/stats - Get overall learning statistics

    /// <summary>
    /// Get overall team learning statistics.
    /// 
    /// Response: StatsResponse with aggregated statistics
    /// </summary>
    [Function("GetStats")]
    public async Task<HttpResponseData> GetStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "stats")] HttpRequestData request)
    {
        // Validate API key
        if (!ValidateApiKey(request))
        {
            return await CreateUnauthorizedResponse(request);
        }

        try
        {
            _logger.LogInformation("Getting learning statistics");

            var tableClient = _tableServiceClient.GetTableClient(TableName);
            
            try
            {
                await tableClient.CreateIfNotExistsAsync();
            }
            catch { /* Ignore if exists */ }

            // Get all feedback (for stats we need everything)
            var allFeedback = new List<FeedbackEntity>();
            await foreach (var entity in tableClient.QueryAsync<FeedbackEntity>())
            {
                allFeedback.Add(entity);
            }

            var totalFeedback = allFeedback.Count;
            var helpfulCount = allFeedback.Count(f => f.IsHelpful);
            var notHelpfulCount = totalFeedback - helpfulCount;
            var helpfulRate = totalFeedback > 0 ? (double)helpfulCount / totalFeedback * 100 : 0;

            // Unique patterns
            var uniquePatterns = allFeedback
                .GroupBy(f => $"{f.Rule}|{f.PartitionKey}|{f.IssueHash}")
                .Count();

            // Unique contributors
            var uniqueContributors = allFeedback
                .Select(f => f.Contributor)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .Count();

            // By extension
            var byExtension = allFeedback
                .GroupBy(f => f.PartitionKey)
                .ToDictionary(g => g.Key, g => g.Count());

            // Top contributors
            var topContributors = allFeedback
                .GroupBy(f => f.Contributor)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new ContributorStat
                {
                    Name = g.Key,
                    FeedbackCount = g.Count()
                })
                .ToList();

            var result = new StatsResponse
            {
                TotalFeedback = totalFeedback,
                HelpfulCount = helpfulCount,
                NotHelpfulCount = notHelpfulCount,
                HelpfulRate = Math.Round(helpfulRate, 1),
                UniquePatterns = uniquePatterns,
                UniqueContributors = uniqueContributors,
                ByExtension = byExtension,
                TopContributors = topContributors
            };

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stats");
            var errorResponse = request.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get stats" });
            return errorResponse;
        }
    }

    // GET /api/health - Health check

    /// <summary>
    /// Health check endpoint (no auth required).
    /// Used to verify the function is running and can connect to storage.
    /// </summary>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request)
    {
        try
        {
            // Try to access table storage
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "1.0.0"
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            var response = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await response.WriteAsJsonAsync(new
            {
                status = "unhealthy",
                error = ex.Message
            });
            return response;
        }
    }

    // Helper Methods

    /// <summary>
    /// Truncates a string if it exceeds the maximum length.
    /// Azure Table Storage has a 64KB limit per property.
    /// </summary>
    private static string TruncateIfNeeded(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength - 3) + "...";
    }
}
