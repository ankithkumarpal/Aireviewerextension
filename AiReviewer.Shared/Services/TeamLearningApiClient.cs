using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// HTTP client for communicating with the Team Learning API (Azure Function).
    /// 
    /// This client is used when Team Learning is enabled in VS settings.
    /// It sends feedback to and retrieves patterns from the shared Azure storage.
    /// </summary>
    public class TeamLearningApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _disposed;

        /// <summary>
        /// Creates a new Team Learning API client.
        /// </summary>
        /// <param name="baseUrl">Base URL of the API (e.g., https://my-func.azurewebsites.net/api)</param>
        /// <param name="apiKey">API key for authentication</param>
        public TeamLearningApiClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Submit Feedback
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Submits feedback to the Team Learning API.
        /// This is called when a user clicks on feedback buttons in the UI.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SubmitFeedbackAsync(TeamFeedbackRequest feedback)
        {
            try
            {
                var url = $"{_baseUrl}/feedback";
                var json = JsonSerializer.Serialize(feedback, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[TeamLearning] Submit failed: {response.StatusCode} - {error}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Feedback submitted successfully");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Submit error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Submits feedback without waiting for the result (fire-and-forget).
        /// Use this to avoid blocking the UI.
        /// </summary>
        public void SubmitFeedbackFireAndForget(TeamFeedbackRequest feedback)
        {
            // Fire and forget - don't await
            _ = Task.Run(async () =>
            {
                try
                {
                    await SubmitFeedbackAsync(feedback).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TeamLearning] Fire-and-forget error: {ex.Message}");
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Get Patterns
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets learned patterns for a specific file extension.
        /// </summary>
        /// <param name="fileExtension">File extension (e.g., ".cs")</param>
        /// <param name="minOccurrences">Minimum occurrences to include</param>
        /// <param name="maxResults">Maximum patterns to return</param>
        /// <param name="minAccuracy">Minimum accuracy percentage</param>
        public async Task<TeamPatternsResponse?> GetPatternsAsync(
            string fileExtension,
            int minOccurrences = 2,
            int maxResults = 15,
            double minAccuracy = 35)
        {
            try
            {
                var ext = Uri.EscapeDataString(fileExtension.ToLowerInvariant());
                var url = $"{_baseUrl}/patterns?ext={ext}&minOccurrences={minOccurrences}&maxResults={maxResults}&minAccuracy={minAccuracy}";

                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[TeamLearning] Get patterns failed: {response.StatusCode} - {error}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TeamPatternsResponse>(json, _jsonOptions);

                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Got {result?.Patterns?.Count ?? 0} patterns for {fileExtension}");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Get patterns error: {ex.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Get Stats
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets overall team learning statistics.
        /// </summary>
        public async Task<TeamStatsResponse?> GetStatsAsync()
        {
            try
            {
                var url = $"{_baseUrl}/stats";
                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[TeamLearning] Get stats failed: {response.StatusCode} - {error}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TeamStatsResponse>(json, _jsonOptions);

                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Got stats: {result?.TotalFeedback ?? 0} total feedback");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TeamLearning] Get stats error: {ex.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Health Check
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks if the API is reachable and healthy.
        /// Can be used for warm-up or connection testing.
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var url = $"{_baseUrl}/health";
                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a warm-up ping to reduce cold start latency.
        /// Call this when the extension loads.
        /// </summary>
        public void WarmUp()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await IsHealthyAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("[TeamLearning] Warm-up ping sent");
                }
                catch { /* Ignore */ }
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Dispose
        // ─────────────────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
