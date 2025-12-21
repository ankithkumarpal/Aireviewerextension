using System.Net;
using System.Text.Json;
using AiReviewer.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiReviewer.Functions.Services;

/// <summary>
/// Azure Function for providing AI configuration.
/// Returns Azure OpenAI credentials to authorized clients.
/// 
/// This keeps OpenAI API keys secure in Azure - clients don't need local credentials!
/// 
/// Endpoint:
///   GET /api/config - Returns OpenAI endpoint, key, and deployment name
/// </summary>
public class ConfigFunctions
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigFunctions> _logger;

    public ConfigFunctions(IConfiguration configuration, ILogger<ConfigFunctions> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/config - Returns Azure OpenAI configuration
    /// 
    /// Response:
    /// {
    ///   "azureOpenAIEndpoint": "https://your-resource.openai.azure.com/",
    ///   "azureOpenAIKey": "your-key",
    ///   "deploymentName": "gpt-4o-mini"
    /// }
    /// 
    /// Security: Azure AD Easy Auth handles authentication and authorization.
    /// Only users in the tenant (including B2B guests) who are assigned to the app can access.
    /// </summary>
    [Function("GetAiConfig")]
    public async Task<HttpResponseData> GetAiConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config")] HttpRequestData request)
    {
        _logger.LogInformation("AI config requested");

        // Authentication & authorization handled by Azure AD Easy Auth
        // If we got here, the user is authenticated and authorized

        var endpoint = _configuration["AzureOpenAIEndpoint"];
        var apiKey = _configuration["AzureOpenAIKey"];
        var deployment = _configuration["AzureOpenAIDeployment"] ?? "gpt-4o-mini";

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("Azure OpenAI not configured in app settings");
            var serverError = request.CreateResponse(HttpStatusCode.InternalServerError);
            await serverError.WriteAsJsonAsync(new { error = "Azure OpenAI not configured on server" });
            return serverError;
        }

        var configResponse = new AiConfigResponse
        {
            AzureOpenAIEndpoint = endpoint,
            AzureOpenAIKey = apiKey,
            DeploymentName = deployment
        };

        _logger.LogInformation("Returning AI config (deployment: {Deployment})", deployment);

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(configResponse);
        return response;
    }
}

