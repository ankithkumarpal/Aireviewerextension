using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Client for calling the server-side review API.
    /// This makes the VSIX extension a thin client - all AI logic runs server-side.
    /// 
    /// Benefits:
    /// - Update prompts/logic without deploying new VSIX
    /// - Centralized AI costs and monitoring
    /// - Easier A/B testing of prompt changes
    /// </summary>
    public class ReviewApiClient
    {
        private readonly string _baseUrl;
        private readonly Func<Task<string>> _tokenProvider;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        /// <summary>
        /// Creates a new ReviewApiClient.
        /// </summary>
        /// <param name="baseUrl">Base URL of the review API (e.g., "https://your-functions.azurewebsites.net")</param>
        /// <param name="tokenProvider">Function that provides the Azure AD access token</param>
        public ReviewApiClient(string baseUrl, Func<Task<string>> tokenProvider)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _tokenProvider = tokenProvider;
        }

        /// <summary>
        /// Submits code patches for server-side AI review.
        /// </summary>
        /// <param name="patches">List of file patches to review</param>
        /// <param name="config">Repository-specific configuration</param>
        /// <param name="repositoryName">Name of the repository</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of review results (issues found)</returns>
        public async Task<List<ReviewResult>> ReviewCodeAsync(
            List<Patch> patches,
            StagebotConfig config,
            string repositoryName,
            CancellationToken cancellationToken = default)
        {
            var request = new ReviewRequest
            {
                Patches = patches.ConvertAll(p => new PatchDto
                {
                    FilePath = p.FilePath,
                    Hunks = p.Hunks.ConvertAll(h => new HunkDto
                    {
                        StartLine = h.StartLine,
                        Lines = h.Lines
                    })
                }),
                Config = new StagebotConfigDto
                {
                    Version = config.Version,
                    IncludePaths = config.IncludePaths,
                    ExcludePaths = config.ExcludePaths,
                    InheritCentralStandards = config.InheritCentralStandards
                },
                RepositoryName = repositoryName
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/review")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // Add authorization header
            var token = await _tokenProvider();
            if (!string.IsNullOrEmpty(token))
            {
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Review API returned {response.StatusCode}: {responseContent}");
            }

            var reviewResponse = JsonSerializer.Deserialize<ReviewResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (reviewResponse == null)
            {
                return new List<ReviewResult>();
            }

            // Convert DTOs back to domain models
            return reviewResponse.Results.ConvertAll(r => new ReviewResult
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
            });
        }
    }

    #region DTOs for API communication

    internal class ReviewRequest
    {
        public List<PatchDto> Patches { get; set; } = new List<PatchDto>();
        public StagebotConfigDto Config { get; set; }
        public string RepositoryName { get; set; } = "";
    }

    internal class PatchDto
    {
        public string FilePath { get; set; } = "";
        public List<HunkDto> Hunks { get; set; } = new List<HunkDto>();
    }

    internal class HunkDto
    {
        public int StartLine { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
    }

    internal class StagebotConfigDto
    {
        public int Version { get; set; } = 1;
        public List<string> IncludePaths { get; set; } = new List<string>();
        public List<string> ExcludePaths { get; set; } = new List<string>();
        public bool InheritCentralStandards { get; set; } = true;
    }

    internal class ReviewResponse
    {
        public List<ReviewResultDto> Results { get; set; } = new List<ReviewResultDto>();
        public int TotalIssues { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }
    }

    internal class ReviewResultDto
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

    #endregion
}
