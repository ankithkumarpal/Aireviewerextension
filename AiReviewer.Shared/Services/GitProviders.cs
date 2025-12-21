using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Interface for Git provider operations (GitHub, Azure DevOps, etc.)
    /// </summary>
    public interface IGitProvider
    {
        /// <summary>
        /// Get PR metadata by number
        /// </summary>
        Task<PrMetadata> GetPullRequestAsync(string owner, string repo, string prNumber);

        /// <summary>
        /// Get the diff/patch content for a PR
        /// </summary>
        Task<string> GetPullRequestDiffAsync(string owner, string repo, string prNumber);

        /// <summary>
        /// Post a review comment on a PR
        /// </summary>
        Task PostReviewCommentAsync(string owner, string repo, string prNumber, ReviewComment comment);

        /// <summary>
        /// Post a general PR comment (not line-specific)
        /// </summary>
        Task PostPrCommentAsync(string owner, string repo, string prNumber, string body);

        /// <summary>
        /// Post a full review with multiple comments
        /// </summary>
        Task PostReviewAsync(string owner, string repo, string prNumber, PrReview review);
    }

    /// <summary>
    /// A line-specific review comment
    /// </summary>
    public class ReviewComment
    {
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string Body { get; set; } = "";
        public string Severity { get; set; } = "Warning";
        /// <summary>
        /// For multi-line comments: start line
        /// </summary>
        public int? StartLine { get; set; }
    }

    /// <summary>
    /// A full PR review submission
    /// </summary>
    public class PrReview
    {
        /// <summary>
        /// Overall review body/summary
        /// </summary>
        public string Body { get; set; } = "";
        /// <summary>
        /// Event type: APPROVE, REQUEST_CHANGES, COMMENT
        /// </summary>
        public string Event { get; set; } = "COMMENT";
        /// <summary>
        /// Line-specific comments
        /// </summary>
        public List<ReviewComment> Comments { get; set; } = new List<ReviewComment>();
    }

    /// <summary>
    /// GitHub API implementation
    /// </summary>
    public class GitHubProvider : IGitProvider
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private const string BaseUrl = "https://api.github.com";

        public GitHubProvider(string token, HttpClient httpClient = null)
        {
            _token = token;
            _http = httpClient ?? new HttpClient();
            _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            _http.DefaultRequestHeaders.Add("User-Agent", "Stagebot-PR-Reviewer");
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<PrMetadata> GetPullRequestAsync(string owner, string repo, string prNumber)
        {
            var url = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var pr = new PrMetadata
            {
                PrNumber = prNumber,
                Title = root.GetProperty("title").GetString() ?? "",
                Description = root.GetProperty("body").GetString() ?? "",
                Author = root.GetProperty("user").GetProperty("login").GetString() ?? "",
                SourceBranch = root.GetProperty("head").GetProperty("ref").GetString() ?? "",
                TargetBranch = root.GetProperty("base").GetProperty("ref").GetString() ?? "",
                TotalAdditions = root.GetProperty("additions").GetInt32(),
                TotalDeletions = root.GetProperty("deletions").GetInt32(),
                Url = root.GetProperty("html_url").GetString() ?? ""
            };

            // Parse labels
            if (root.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    pr.Labels.Add(label.GetProperty("name").GetString() ?? "");
                }
            }

            // Get files changed
            var filesUrl = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}/files";
            var filesResponse = await _http.GetAsync(filesUrl);
            if (filesResponse.IsSuccessStatusCode)
            {
                var filesJson = await filesResponse.Content.ReadAsStringAsync();
                using var filesDoc = JsonDocument.Parse(filesJson);
                foreach (var file in filesDoc.RootElement.EnumerateArray())
                {
                    pr.FilesChanged.Add(file.GetProperty("filename").GetString() ?? "");
                }
            }

            return pr;
        }

        public async Task<string> GetPullRequestDiffAsync(string owner, string repo, string prNumber)
        {
            var url = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/vnd.github.diff");
            request.Headers.Add("Authorization", $"Bearer {_token}");
            request.Headers.Add("User-Agent", "Stagebot-PR-Reviewer");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task PostReviewCommentAsync(string owner, string repo, string prNumber, ReviewComment comment)
        {
            // For single inline comments, use the review comments endpoint
            var url = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}/comments";
            
            // First, get the latest commit SHA
            var prUrl = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}";
            var prResponse = await _http.GetAsync(prUrl);
            var prJson = await prResponse.Content.ReadAsStringAsync();
            using var prDoc = JsonDocument.Parse(prJson);
            var commitSha = prDoc.RootElement.GetProperty("head").GetProperty("sha").GetString();

            var body = new
            {
                body = FormatCommentBody(comment),
                commit_id = commitSha,
                path = comment.FilePath,
                line = comment.LineNumber,
                side = "RIGHT"
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        public async Task PostPrCommentAsync(string owner, string repo, string prNumber, string body)
        {
            var url = $"{BaseUrl}/repos/{owner}/{repo}/issues/{prNumber}/comments";
            var payload = new { body };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        public async Task PostReviewAsync(string owner, string repo, string prNumber, PrReview review)
        {
            var url = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}/reviews";

            // Get latest commit SHA
            var prUrl = $"{BaseUrl}/repos/{owner}/{repo}/pulls/{prNumber}";
            var prResponse = await _http.GetAsync(prUrl);
            var prJson = await prResponse.Content.ReadAsStringAsync();
            using var prDoc = JsonDocument.Parse(prJson);
            var commitSha = prDoc.RootElement.GetProperty("head").GetProperty("sha").GetString();

            var comments = new List<object>();
            foreach (var c in review.Comments)
            {
                comments.Add(new
                {
                    path = c.FilePath,
                    line = c.LineNumber,
                    body = FormatCommentBody(c),
                    side = "RIGHT"
                });
            }

            var body = new
            {
                commit_id = commitSha,
                body = review.Body,
                @event = review.Event,
                comments
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        private string FormatCommentBody(ReviewComment comment)
        {
            var sev = comment.Severity?.ToLower() ?? "";
            string emoji;
            if (sev == "error" || sev == "high")
                emoji = "🔴";
            else if (sev == "warning" || sev == "medium")
                emoji = "🟡";
            else
                emoji = "🔵";
            return $"{emoji} **{comment.Severity}**: {comment.Body}";
        }
    }

    /// <summary>
    /// Azure DevOps API implementation
    /// </summary>
    public class AzureDevOpsProvider : IGitProvider
    {
        private readonly HttpClient _http;
        private readonly string _organization;
        private readonly string _project;

        public AzureDevOpsProvider(string token, string organization, string project, HttpClient httpClient = null)
        {
            _organization = organization;
            _project = project;
            _http = httpClient ?? new HttpClient();
            
            var authBytes = Encoding.ASCII.GetBytes($":{token}");
            _http.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(authBytes)}");
        }

        public async Task<PrMetadata> GetPullRequestAsync(string owner, string repo, string prNumber)
        {
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}?api-version=7.0";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var pr = new PrMetadata
            {
                PrNumber = prNumber,
                Title = root.GetProperty("title").GetString() ?? "",
                Description = root.GetProperty("description").GetString() ?? "",
                Author = root.GetProperty("createdBy").GetProperty("displayName").GetString() ?? "",
                SourceBranch = root.GetProperty("sourceRefName").GetString()?.Replace("refs/heads/", "") ?? "",
                TargetBranch = root.GetProperty("targetRefName").GetString()?.Replace("refs/heads/", "") ?? "",
                Url = root.GetProperty("url").GetString() ?? ""
            };

            // Get labels
            if (root.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    pr.Labels.Add(label.GetProperty("name").GetString() ?? "");
                }
            }

            return pr;
        }

        public async Task<string> GetPullRequestDiffAsync(string owner, string repo, string prNumber)
        {
            // Azure DevOps doesn't have a direct diff endpoint like GitHub
            // We need to get iterations and then diffs
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}/iterations?api-version=7.0";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var iterations = doc.RootElement.GetProperty("value");
            var lastIteration = iterations[iterations.GetArrayLength() - 1].GetProperty("id").GetInt32();

            // Get changes for the last iteration
            var changesUrl = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}/iterations/{lastIteration}/changes?api-version=7.0";
            var changesResponse = await _http.GetAsync(changesUrl);
            changesResponse.EnsureSuccessStatusCode();

            return await changesResponse.Content.ReadAsStringAsync();
        }

        public async Task PostReviewCommentAsync(string owner, string repo, string prNumber, ReviewComment comment)
        {
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}/threads?api-version=7.0";
            
            var body = new
            {
                comments = new[]
                {
                    new { content = FormatCommentBody(comment), commentType = 1 }
                },
                status = 1, // Active
                threadContext = new
                {
                    filePath = "/" + comment.FilePath,
                    rightFileStart = new { line = comment.LineNumber, offset = 1 },
                    rightFileEnd = new { line = comment.LineNumber, offset = 1 }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        public async Task PostPrCommentAsync(string owner, string repo, string prNumber, string body)
        {
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repo}/pullrequests/{prNumber}/threads?api-version=7.0";
            
            var payload = new
            {
                comments = new[]
                {
                    new { content = body, commentType = 1 }
                },
                status = 1
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        public async Task PostReviewAsync(string owner, string repo, string prNumber, PrReview review)
        {
            // Post summary comment first
            if (!string.IsNullOrEmpty(review.Body))
            {
                await PostPrCommentAsync(owner, repo, prNumber, review.Body);
            }

            // Post individual line comments
            foreach (var comment in review.Comments)
            {
                await PostReviewCommentAsync(owner, repo, prNumber, comment);
            }
        }

        private string FormatCommentBody(ReviewComment comment)
        {
            var sev = comment.Severity?.ToLower() ?? "";
            string emoji;
            if (sev == "error" || sev == "high")
                emoji = "🔴";
            else if (sev == "warning" || sev == "medium")
                emoji = "🟡";
            else
                emoji = "🔵";
            return $"{emoji} **{comment.Severity}**: {comment.Body}";
        }
    }
}
