using Microsoft.AspNetCore.Mvc;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.Services;

namespace AiReviewer.ReviewService.Controllers;

/// <summary>
/// Code Review API Controller
/// 
/// This is the main entry point for the VSIX extension to request code reviews.
/// All AI logic runs server-side, making the extension a thin client.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReviewController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReviewController> _logger;

    public ReviewController(IConfiguration configuration, ILogger<ReviewController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Submit code patches for AI review.
    /// </summary>
    /// <param name="request">Review request with patches and configuration</param>
    /// <returns>Review results with issues found</returns>
    [HttpPost]
    public async Task<ActionResult<ReviewResponse>> SubmitReview([FromBody] ReviewRequest request)
    {
        _logger.LogInformation("Received code review request");

        try
        {
            // Validate request
            var validationErrors = ValidateReviewRequest(request);
            if (validationErrors.Count > 0)
            {
                return BadRequest(new { error = "Validation failed", details = validationErrors });
            }

            // Convert DTOs to domain models
            var patches = ConvertToPatches(request.Patches);
            var config = ConvertToConfig(request.Config);

            _logger.LogInformation("Reviewing {FileCount} files for repository: {Repository}", 
                patches.Count, request.RepositoryName);

            // Get AI configuration from Azure Functions (centralized config)
            // This keeps credentials secure in Azure - no local config needed!
            var configApiUrl = _configuration["ConfigApiUrl"];
            if (string.IsNullOrEmpty(configApiUrl))
            {
                _logger.LogError("ConfigApiUrl is not configured");
                return StatusCode(500, new { error = "Config API URL is not configured" });
            }

            string endpoint, apiKey, deploymentName;
            try
            {
                using var httpClient = new HttpClient();
                
                // Forward the Authorization header from the incoming request to Azure Functions
                if (Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", authHeader.ToString());
                    _logger.LogDebug("Forwarding auth token to Azure Functions");
                }
                else
                {
                    _logger.LogWarning("No Authorization header found in request");
                }
                
                var configResponse = await httpClient.GetAsync($"{configApiUrl}/config");
                
                if (!configResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to fetch AI config from Azure Functions: {Status}", configResponse.StatusCode);
                    return StatusCode(500, new { error = "Failed to fetch AI configuration" });
                }

                var configJson = await configResponse.Content.ReadAsStringAsync();
                var aiConfig = System.Text.Json.JsonSerializer.Deserialize<AiConfigDto>(configJson, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (aiConfig == null || string.IsNullOrEmpty(aiConfig.AzureOpenAIEndpoint) || string.IsNullOrEmpty(aiConfig.AzureOpenAIKey))
                {
                    _logger.LogError("Invalid AI config received from Azure Functions");
                    return StatusCode(500, new { error = "Invalid AI configuration" });
                }

                endpoint = aiConfig.AzureOpenAIEndpoint;
                apiKey = aiConfig.AzureOpenAIKey;
                deploymentName = aiConfig.DeploymentName ?? "gpt-4o-mini";
                
                _logger.LogDebug("Fetched AI config from Azure Functions (deployment: {Deployment})", deploymentName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AI config from Azure Functions");
                return StatusCode(500, new { error = "Failed to fetch AI configuration" });
            }

            // Create AI review service
            var aiService = new AiReviewService(endpoint, apiKey, deploymentName, _logger);
            
            // For calling Azure Functions, we don't need auth tokens (internal calls)
            Func<Task<string>> noAuthTokenProvider = () => Task.FromResult(string.Empty);
            
            // Set up StandardsService to fetch standards from Azure Functions
            var standardsApiUrl = _configuration["StandardsApiUrl"];
            if (!string.IsNullOrEmpty(standardsApiUrl))
            {
                var standardsService = new StandardsService(standardsApiUrl, noAuthTokenProvider);
                aiService.SetStandardsService(standardsService);
                _logger.LogDebug("StandardsService configured with URL: {Url}", standardsApiUrl);
            }

            // Set up TeamLearningApiClient to fetch patterns from Azure Functions
            var teamLearningEnabled = _configuration.GetValue<bool>("EnableTeamLearning", true);
            var teamLearningApiUrl = _configuration["TeamLearningApiUrl"];
            if (teamLearningEnabled && !string.IsNullOrEmpty(teamLearningApiUrl))
            {
                var teamApiClient = new TeamLearningApiClient(teamLearningApiUrl, noAuthTokenProvider);
                aiService.SetTeamApiClient(teamApiClient);
                _logger.LogDebug("TeamLearningApiClient configured with URL: {Url}", teamLearningApiUrl);
            }

            // Perform the review
            var results = await aiService.ReviewCodeAsync(patches, config, request.RepositoryName);

            _logger.LogInformation("Review completed with {IssueCount} issues found", results.Count);

            // Build response
            var reviewResponse = new ReviewResponse
            {
                Results = results.Select(r => new ReviewResultDto
                {
                    ReviewId = r.ReviewId,
                    FilePath = r.FilePath,
                    LineNumber = r.LineNumber,
                    Severity = r.Severity,
                    Confidence = r.Confidence,
                    Issue = r.Issue,
                    Suggestion = r.Suggestion,
                    Rule = r.Rule,
                    CheckId = r.CheckId,
                    RuleSource = r.RuleSource,
                    CodeSnippet = r.CodeSnippet,
                    FixedCode = r.FixedCode
                }).ToList(),
                TotalIssues = results.Count,
                HighCount = results.Count(r => r.Severity == "High"),
                MediumCount = results.Count(r => r.Severity == "Medium"),
                LowCount = results.Count(r => r.Severity == "Low")
            };

            return Ok(reviewResponse);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Review was cancelled");
            return StatusCode(408, new { error = "Review was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing code review");
            return StatusCode(500, new { error = "Failed to perform review" });
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", service = "AiReviewer.ReviewService" });
    }

    #region Validation & Conversion

    private static List<string> ValidateReviewRequest(ReviewRequest request)
    {
        var errors = new List<string>();

        if (request.Patches == null || request.Patches.Count == 0)
        {
            errors.Add("At least one patch is required");
        }
        else
        {
            foreach (var patch in request.Patches)
            {
                if (string.IsNullOrWhiteSpace(patch.FilePath))
                {
                    errors.Add("Patch filePath is required");
                }
                if (patch.Hunks == null || patch.Hunks.Count == 0)
                {
                    errors.Add($"Patch '{patch.FilePath}' must have at least one hunk");
                }
            }
        }

        return errors;
    }

    private static List<Patch> ConvertToPatches(List<PatchDto> dtos)
    {
        return dtos.Select(dto => new Patch(
            dto.FilePath,
            dto.Hunks.Select(h => new Hunk(h.StartLine, h.Lines)).ToList()
        )).ToList();
    }

    private static StagebotConfig ConvertToConfig(StagebotConfigDto? dto)
    {
        if (dto == null)
        {
            return new StagebotConfig();
        }

        return new StagebotConfig
        {
            Version = dto.Version,
            IncludePaths = dto.IncludePaths ?? new List<string>(),
            ExcludePaths = dto.ExcludePaths ?? new List<string>(),
            InheritCentralStandards = dto.InheritCentralStandards
        };
    }

    #endregion
}

#region Request/Response DTOs

/// <summary>
/// Request body for POST /api/review
/// </summary>
public class ReviewRequest
{
    public List<PatchDto> Patches { get; set; } = new();
    public StagebotConfigDto? Config { get; set; }
    public string RepositoryName { get; set; } = "";
}

public class PatchDto
{
    public string FilePath { get; set; } = "";
    public List<HunkDto> Hunks { get; set; } = new();
}

public class HunkDto
{
    public int StartLine { get; set; }
    public List<string> Lines { get; set; } = new();
}

public class StagebotConfigDto
{
    public int Version { get; set; } = 1;
    public List<string>? IncludePaths { get; set; }
    public List<string>? ExcludePaths { get; set; }
    public bool InheritCentralStandards { get; set; } = true;
}

/// <summary>
/// Response from POST /api/review
/// </summary>
public class ReviewResponse
{
    public List<ReviewResultDto> Results { get; set; } = new();
    public int TotalIssues { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
}

public class ReviewResultDto
{
    public string ReviewId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string Severity { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string Issue { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public string Rule { get; set; } = "";
    public string CheckId { get; set; } = "";
    public string RuleSource { get; set; } = "";
    public string CodeSnippet { get; set; } = "";
    public string FixedCode { get; set; } = "";
}

/// <summary>
/// DTO for AI config response from Azure Functions /api/config
/// </summary>
public class AiConfigDto
{
    public string AzureOpenAIEndpoint { get; set; } = "";
    public string AzureOpenAIKey { get; set; } = "";
    public string DeploymentName { get; set; } = "gpt-4o-mini";
}

#endregion
