using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.StaticHelper;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiReviewer.Functions
{
    /// <summary>
    /// Azure Table Storage entity for NNF-wide coding standards
    /// </summary>
    public class NnfStandardEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "NNF";
        public string RowKey { get; set; } = "current";
        public string YamlContent { get; set; } = "";
        public int Version { get; set; } = 1;
        public string UpdatedBy { get; set; } = "";
        public string ChangeDescription { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    /// <summary>
    /// Azure Functions for managing NNF coding standards
    /// </summary>
    public class StandardsFunctions
    {
        private readonly ILogger<StandardsFunctions> _logger;
        private readonly TableClient _tableClient;

        public StandardsFunctions(ILogger<StandardsFunctions> logger)
        {
            _logger = logger;
            
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") 
                ?? "UseDevelopmentStorage=true";
            var tableServiceClient = new TableServiceClient(connectionString);
            _tableClient = tableServiceClient.GetTableClient("NnfStandards");
            _tableClient.CreateIfNotExists();
        }

        /// <summary>
        /// GET /api/standards/nnf - Get current NNF standards
        /// </summary>
        [Function("GetNnfStandards")]
        public async Task<HttpResponseData> GetNnfStandards(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "standards/nnf")] 
            HttpRequestData req)
        {
            _logger.LogInformation("Getting NNF standards");

            try
            {
                var entity = await _tableClient.GetEntityIfExistsAsync<NnfStandardEntity>("NNF", "current");
                
                if (!entity.HasValue)
                {
                    _logger.LogInformation("No NNF standards found, returning defaults");
                    
                    // Return embedded defaults
                    var defaults = EmbeddedDefaults();
                    var defaultResponse = req.CreateResponse(HttpStatusCode.OK);
                    await defaultResponse.WriteAsJsonAsync(new NnfStandardsResponse
                    {
                        YamlContent = defaults,
                        Version = 0,
                        UpdatedBy = "system",
                        UpdatedAt = DateTime.UtcNow,
                        ParsedConfig = ParseYaml(defaults)
                    });
                    return defaultResponse;
                }

                var standard = entity.Value;
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new NnfStandardsResponse
                {
                    YamlContent = standard.YamlContent,
                    Version = standard.Version,
                    UpdatedBy = standard.UpdatedBy,
                    UpdatedAt = standard.Timestamp?.DateTime ?? DateTime.UtcNow,
                    ParsedConfig = ParseYaml(standard.YamlContent)
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting NNF standards");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        /// <summary>
        /// PUT /api/standards/nnf - Update NNF standards
        /// </summary>
        [Function("UpdateNnfStandards")]
        public async Task<HttpResponseData> UpdateNnfStandards(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "standards/nnf")] 
            HttpRequestData req)
        {
            _logger.LogInformation("Updating NNF standards");

            try
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonSerializer.Deserialize<UpdateNnfStandardsRequest>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (string.IsNullOrEmpty(request?.YamlContent))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "YamlContent is required" });
                    return badRequest;
                }

                // Validate YAML
                StagebotConfig parsed;
                try
                {
                    parsed = ParseYaml(request.YamlContent);
                }
                catch (Exception ex)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"Invalid YAML: {ex.Message}" });
                    return badRequest;
                }

                // Get current version
                var existing = await _tableClient.GetEntityIfExistsAsync<NnfStandardEntity>("NNF", "current");
                var newVersion = existing.HasValue ? existing.Value.Version + 1 : 1;

                // Save current as history (if exists)
                if (existing.HasValue)
                {
                    var historyEntity = new NnfStandardEntity
                    {
                        PartitionKey = "NNF",
                        RowKey = $"v{existing.Value.Version}",
                        YamlContent = existing.Value.YamlContent,
                        Version = existing.Value.Version,
                        UpdatedBy = existing.Value.UpdatedBy,
                        ChangeDescription = existing.Value.ChangeDescription
                    };
                    await _tableClient.UpsertEntityAsync(historyEntity);
                }

                // Save new current
                var newEntity = new NnfStandardEntity
                {
                    PartitionKey = "NNF",
                    RowKey = "current",
                    YamlContent = request.YamlContent,
                    Version = newVersion,
                    UpdatedBy = request.UpdatedBy ?? "unknown",
                    ChangeDescription = request.ChangeDescription ?? ""
                };
                await _tableClient.UpsertEntityAsync(newEntity);

                _logger.LogInformation($"NNF standards updated to v{newVersion} by {request.UpdatedBy}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new NnfStandardsResponse
                {
                    YamlContent = request.YamlContent,
                    Version = newVersion,
                    UpdatedBy = request.UpdatedBy ?? "unknown",
                    UpdatedAt = DateTime.UtcNow,
                    ParsedConfig = parsed
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating NNF standards");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        /// <summary>
        /// GET /api/standards/nnf/version/{version} - Get a specific version's full content
        /// </summary>
        [Function("GetNnfStandardsByVersion")]
        public async Task<HttpResponseData> GetNnfStandardsByVersion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "standards/nnf/version/{version}")] 
            HttpRequestData req,
            string version)
        {
            _logger.LogInformation($"Getting NNF standards version: {version}");

            try
            {
                // Determine the RowKey based on version parameter
                string rowKey;
                if (version.Equals("current", StringComparison.OrdinalIgnoreCase) || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
                {
                    rowKey = "current";
                }
                else if (int.TryParse(version, out int versionNumber))
                {
                    // If it's a number, check if it's the current version or a historical one
                    var currentEntity = await _tableClient.GetEntityIfExistsAsync<NnfStandardEntity>("NNF", "current");
                    if (currentEntity.HasValue && currentEntity.Value.Version == versionNumber)
                    {
                        rowKey = "current";
                    }
                    else
                    {
                        rowKey = $"v{versionNumber}";
                    }
                }
                else if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    rowKey = version.ToLower(); // e.g., "v1", "v2"
                }
                else
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid version format. Use 'current', 'latest', a number (e.g., '1'), or 'v1' format." });
                    return badRequest;
                }

                var entity = await _tableClient.GetEntityIfExistsAsync<NnfStandardEntity>("NNF", rowKey);

                if (!entity.HasValue)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = $"Version '{version}' not found" });
                    return notFound;
                }

                var standard = entity.Value;
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new NnfStandardsResponse
                {
                    YamlContent = standard.YamlContent,
                    Version = standard.Version,
                    UpdatedBy = standard.UpdatedBy,
                    UpdatedAt = standard.Timestamp?.DateTime ?? DateTime.UtcNow,
                    ParsedConfig = ParseYaml(standard.YamlContent)
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting NNF standards version {version}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        /// <summary>
        /// GET /api/standards/nnf/history - Get version history
        /// </summary>
        [Function("GetNnfStandardsHistory")]
        public async Task<HttpResponseData> GetNnfStandardsHistory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "standards/nnf/history")] 
            HttpRequestData req)
        {
            _logger.LogInformation("Getting NNF standards history");

            try
            {
                var history = new System.Collections.Generic.List<object>();
                
                await foreach (var entity in _tableClient.QueryAsync<NnfStandardEntity>(e => e.PartitionKey == "NNF"))
                {
                    history.Add(new
                    {
                        version = entity.Version,
                        rowKey = entity.RowKey,
                        updatedBy = entity.UpdatedBy,
                        updatedAt = entity.Timestamp?.DateTime,
                        changeDescription = entity.ChangeDescription,
                        isCurrent = entity.RowKey == "current"
                    });
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(history);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting NNF standards history");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        private StagebotConfig ParseYaml(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            return deserializer.Deserialize<StagebotConfig>(yaml) ?? new StagebotConfig();
        }

        private string EmbeddedDefaults()
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var defaults = AiReviewer.Shared.EmbeddedStandards.GetDefaults();
            return serializer.Serialize(defaults);
        }
    }
}
