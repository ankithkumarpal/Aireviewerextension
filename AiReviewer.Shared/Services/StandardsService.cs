using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.StaticHelper;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Service to fetch and cache NNF standards from the API
    /// </summary>
    public class StandardsService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly Func<Task<string>> _tokenProvider;

        // Cache
        private static StagebotConfig _cachedNnfStandards;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        public StandardsService(string baseUrl, Func<Task<string>> tokenProvider, HttpClient httpClient = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _tokenProvider = tokenProvider;
            _http = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Get NNF standards from API (with caching)
        /// </summary>
        public async Task<StagebotConfig> GetNnfStandardsAsync(bool forceRefresh = false)
        {
            // Return cached if valid
            if (!forceRefresh && _cachedNnfStandards != null && DateTime.UtcNow < _cacheExpiry)
            {
                System.Diagnostics.Debug.WriteLine("[Standards] Using cached NNF standards");
                return _cachedNnfStandards;
            }

            try
            {
                // Set auth header
                var token = await _tokenProvider();
                _http.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await _http.GetAsync($"{_baseUrl}/api/standards/nnf");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<NnfStandardsResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result?.ParsedConfig != null)
                    {
                        _cachedNnfStandards = result.ParsedConfig;
                        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
                        System.Diagnostics.Debug.WriteLine($"[Standards] Fetched NNF standards v{result.Version}");
                        return _cachedNnfStandards;
                    }

                    // If only YAML returned, parse it
                    if (!string.IsNullOrEmpty(result?.YamlContent))
                    {
                        _cachedNnfStandards = ParseYaml(result.YamlContent);
                        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
                        return _cachedNnfStandards;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Standards] API returned {response.StatusCode}, using fallback");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Standards] Failed to fetch NNF standards: {ex.Message}");
            }

            // Return cached if available, else null (will use embedded defaults)
            return _cachedNnfStandards;
        }

        /// <summary>
        /// Update NNF standards via API
        /// </summary>
        public async Task<bool> UpdateNnfStandardsAsync(string yamlContent, string updatedBy, string changeDescription)
        {
            try
            {
                var token = await _tokenProvider();
                _http.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var request = new UpdateNnfStandardsRequest
                {
                    YamlContent = yamlContent,
                    UpdatedBy = updatedBy,
                    ChangeDescription = changeDescription
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var response = await _http.PutAsync($"{_baseUrl}/api/standards/nnf", content);
                
                if (response.IsSuccessStatusCode)
                {
                    // Invalidate cache
                    _cachedNnfStandards = null;
                    _cacheExpiry = DateTime.MinValue;
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"[Standards] Update failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Standards] Update error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get fully merged config: Embedded → NNF → Repo
        /// </summary>
        public async Task<StagebotConfig> GetMergedConfigAsync(string repositoryPath)
        {
            // 1. Start with embedded defaults
            var config = EmbeddedStandards.GetDefaults();
            System.Diagnostics.Debug.WriteLine("[Standards] Loaded embedded defaults");

            // 2. Merge NNF standards (if available)
            var nnfStandards = await GetNnfStandardsAsync();
            if (nnfStandards != null)
            {
                config = StagebotConfigLoader.MergeConfigs(config, nnfStandards);
                System.Diagnostics.Debug.WriteLine("[Standards] Merged NNF standards");
            }

            // 3. Merge repo-specific config (if exists)
            if (!string.IsNullOrEmpty(repositoryPath))
            {
                var repoConfig = StagebotConfigLoader.LoadFromRepository(repositoryPath);
                if (repoConfig != null)
                {
                    config = StagebotConfigLoader.MergeConfigs(config, repoConfig);
                    System.Diagnostics.Debug.WriteLine("[Standards] Merged repo-specific config");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Standards] Final config: {config.Checks?.Count ?? 0} checks");
            return config;
        }

        /// <summary>
        /// Clear the cache (useful for testing or manual refresh)
        /// </summary>
        public static void ClearCache()
        {
            _cachedNnfStandards = null;
            _cacheExpiry = DateTime.MinValue;
        }

        private StagebotConfig ParseYaml(string yaml)
        {
            try
            {
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                    .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                return deserializer.Deserialize<StagebotConfig>(yaml) ?? new StagebotConfig();
            }
            catch
            {
                return new StagebotConfig();
            }
        }
    }
}
