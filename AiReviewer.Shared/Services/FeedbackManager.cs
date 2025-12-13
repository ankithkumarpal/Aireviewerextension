using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Manages storage and retrieval of user feedback via Azure Team Learning API.
    /// All feedback is stored in Azure Table Storage (no local files).
    /// </summary>
    public class FeedbackManager
    {
        private readonly string _repositoryPath;
        private readonly TeamLearningApiClient _teamApiClient;
        private readonly string _contributorName;

        /// <summary>
        /// Creates a new FeedbackManager with team learning API
        /// </summary>
        /// <param name="repositoryPath">Root path of the git repository</param>
        /// <param name="teamApiClient">API client for team learning (required)</param>
        /// <param name="contributorName">Name to identify this user's feedback</param>
        public FeedbackManager(string repositoryPath, TeamLearningApiClient teamApiClient, string contributorName)
        {
            _repositoryPath = repositoryPath ?? throw new ArgumentNullException(nameof(repositoryPath));
            _teamApiClient = teamApiClient ?? throw new ArgumentNullException(nameof(teamApiClient));
            _contributorName = string.IsNullOrEmpty(contributorName) ? Environment.UserName : contributorName;
        }

        /// <summary>
        /// Saves feedback to Azure (fire-and-forget for UI responsiveness)
        /// </summary>
        /// <param name="feedback">The feedback to save</param>
        public void SaveFeedback(ReviewFeedback feedback)
        {
            feedback.RepositoryPath = _repositoryPath;
            feedback.Timestamp = DateTime.UtcNow;

            var teamFeedback = ConvertToTeamFeedback(feedback);
            _teamApiClient.SubmitFeedbackFireAndForget(teamFeedback);
            
            System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Feedback sent to Azure: {feedback.AiIssueDescription} - WasHelpful: {feedback.WasHelpful}");
        }

        /// <summary>
        /// Saves feedback to Azure and waits for completion
        /// </summary>
        public async Task SaveFeedbackAsync(ReviewFeedback feedback)
        {
            feedback.RepositoryPath = _repositoryPath;
            feedback.Timestamp = DateTime.UtcNow;

            var teamFeedback = ConvertToTeamFeedback(feedback);
            await _teamApiClient.SubmitFeedbackAsync(teamFeedback).ConfigureAwait(false);
            
            System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Feedback saved to Azure: {feedback.AiIssueDescription}");
        }

        /// <summary>
        /// Converts local feedback to team API format
        /// </summary>
        private TeamFeedbackRequest ConvertToTeamFeedback(ReviewFeedback feedback)
        {
            return new TeamFeedbackRequest
            {
                FileExtension = Path.GetExtension(feedback.FilePath ?? "").ToLowerInvariant(),
                Rule = feedback.Rule ?? "GENERAL",
                CodeSnippet = feedback.CodeSnippet ?? "",
                Suggestion = feedback.AiIssueDescription ?? "",
                IssueHash = ComputeIssueHash(feedback.AiIssueDescription),
                IsHelpful = feedback.WasHelpful,
                Reason = feedback.Reason,
                Correction = feedback.UserCorrection,
                Contributor = _contributorName,
                Repository = Path.GetFileName(_repositoryPath)
            };
        }

        /// <summary>
        /// Computes a hash for grouping similar suggestions
        /// </summary>
        private static string ComputeIssueHash(string? issueDescription)
        {
            if (string.IsNullOrEmpty(issueDescription))
                return "unknown";

            var normalized = issueDescription.ToLowerInvariant().Trim();
            if (normalized.Length > 100)
                normalized = normalized.Substring(0, 100);

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(normalized);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Creates feedback from a review result
        /// </summary>
        public ReviewFeedback CreateFeedback(
            string filePath,
            int lineNumber,
            string issueDescription,
            string severity,
            string rule,
            string codeSnippet,
            bool wasHelpful,
            string userCorrection = "",
            string reason = "")
        {
            return new ReviewFeedback
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                AiIssueDescription = issueDescription,
                Severity = severity,
                Rule = rule,
                CodeSnippet = codeSnippet,
                WasHelpful = wasHelpful,
                UserCorrection = userCorrection,
                Reason = reason,
                RepositoryPath = _repositoryPath,
                ProjectName = Path.GetFileName(_repositoryPath),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Gets feedback statistics from Azure
        /// </summary>
        public FeedbackStats GetStats()
        {
            try
            {
                return GetStatsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Failed to get stats: {ex.Message}");
                return new FeedbackStats();
            }
        }

        /// <summary>
        /// Gets feedback statistics from Azure asynchronously
        /// </summary>
        public async Task<FeedbackStats> GetStatsAsync()
        {
            var teamStats = await _teamApiClient.GetStatsAsync().ConfigureAwait(false);
            
            if (teamStats == null)
            {
                return new FeedbackStats();
            }

            return new FeedbackStats
            {
                TotalFeedback = teamStats.TotalFeedback,
                HelpfulCount = teamStats.HelpfulCount,
                NotHelpfulCount = teamStats.NotHelpfulCount,
                AccuracyPercent = Math.Round(teamStats.HelpfulRate, 1),
                TotalReviews = teamStats.UniquePatterns,
                LastUpdated = DateTime.UtcNow,
                IsTeamStats = true,
                UniqueContributors = teamStats.UniqueContributors,
                TopContributors = teamStats.TopContributors
            };
        }
    }

    /// <summary>
    /// Statistics about collected feedback
    /// </summary>
    public class FeedbackStats
    {
        public int TotalFeedback { get; set; }
        public int HelpfulCount { get; set; }
        public int NotHelpfulCount { get; set; }
        public double AccuracyPercent { get; set; }
        public int TotalReviews { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsTeamStats { get; set; } = true;
        public int UniqueContributors { get; set; }
        public List<TeamContributorStat>? TopContributors { get; set; }
    }
}
