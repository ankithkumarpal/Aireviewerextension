using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Manages storage and retrieval of user feedback for the AI learning system.
    /// Stores feedback in a JSON file within the repository's .config folder.
    /// </summary>
    public class FeedbackManager
    {
        private static readonly string FeedbackFileName = "feedback.json";
        private static readonly string ConfigFolder = ".config/ai-reviewer";
        
        private readonly string _repositoryPath;
        private readonly string _feedbackFilePath;

        /// <summary>
        /// Creates a new FeedbackManager for the specified repository
        /// </summary>
        /// <param name="repositoryPath">Root path of the git repository</param>
        public FeedbackManager(string repositoryPath)
        {
            _repositoryPath = repositoryPath;
            _feedbackFilePath = GetFeedbackFilePath(repositoryPath);
            EnsureDirectoryExists();
        }

        /// <summary>
        /// Gets the full path to the feedback file
        /// </summary>
        public static string GetFeedbackFilePath(string repositoryPath)
        {
            return Path.Combine(repositoryPath, ConfigFolder, FeedbackFileName);
        }

        /// <summary>
        /// Ensures the config directory exists
        /// </summary>
        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_feedbackFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Loads all feedback from the JSON file
        /// </summary>
        /// <returns>FeedbackData containing all feedback entries</returns>
        public FeedbackData LoadFeedback()
        {
            try
            {
                if (!File.Exists(_feedbackFilePath))
                {
                    return new FeedbackData();
                }

                var json = File.ReadAllText(_feedbackFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var data = JsonSerializer.Deserialize<FeedbackData>(json, options);
                return data ?? new FeedbackData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading feedback: {ex.Message}");
                return new FeedbackData();
            }
        }

        /// <summary>
        /// Saves a single feedback entry (appends to existing data)
        /// </summary>
        /// <param name="feedback">The feedback to save</param>
        public void SaveFeedback(ReviewFeedback feedback)
        {
            var data = LoadFeedback();
            
            // Set repository context
            feedback.RepositoryPath = _repositoryPath;
            feedback.Timestamp = DateTime.UtcNow;
            
            // Add new feedback
            data.Feedbacks.Add(feedback);
            data.TotalReviews = GetUniqueReviewCount(data.Feedbacks);
            data.LastUpdated = DateTime.UtcNow;
            
            SaveAllFeedback(data);
            
            System.Diagnostics.Debug.WriteLine($"Feedback saved: {feedback.AiIssueDescription} - WasHelpful: {feedback.WasHelpful}");
        }

        /// <summary>
        /// Saves the complete feedback data to file
        /// </summary>
        /// <param name="data">All feedback data to save</param>
        public void SaveAllFeedback(FeedbackData data)
        {
            try
            {
                EnsureDirectoryExists();
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_feedbackFilePath, json);
                
                System.Diagnostics.Debug.WriteLine($"Feedback file saved: {data.Feedbacks.Count} entries");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving feedback: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates feedback from a review result with helpful status
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
        /// Gets count of unique review sessions
        /// </summary>
        private int GetUniqueReviewCount(List<ReviewFeedback> feedbacks)
        {
            var uniqueDates = new HashSet<string>();
            foreach (var f in feedbacks)
            {
                // Group by day and file
                uniqueDates.Add($"{f.Timestamp:yyyy-MM-dd}_{f.FilePath}");
            }
            return uniqueDates.Count;
        }

        /// <summary>
        /// Gets feedback statistics for display
        /// </summary>
        public FeedbackStats GetStats()
        {
            var data = LoadFeedback();
            
            var helpfulCount = 0;
            var notHelpfulCount = 0;
            
            foreach (var f in data.Feedbacks)
            {
                if (f.WasHelpful)
                    helpfulCount++;
                else
                    notHelpfulCount++;
            }
            
            var totalFeedback = data.Feedbacks.Count;
            var accuracy = totalFeedback > 0 
                ? (double)helpfulCount / totalFeedback * 100 
                : 0;

            return new FeedbackStats
            {
                TotalFeedback = totalFeedback,
                HelpfulCount = helpfulCount,
                NotHelpfulCount = notHelpfulCount,
                AccuracyPercent = Math.Round(accuracy, 1),
                TotalReviews = data.TotalReviews,
                LastUpdated = data.LastUpdated
            };
        }

        /// <summary>
        /// Clears all feedback (use with caution!)
        /// </summary>
        public void ClearAllFeedback()
        {
            SaveAllFeedback(new FeedbackData());
        }

        /// <summary>
        /// Checks if feedback file exists
        /// </summary>
        public bool HasFeedback()
        {
            return File.Exists(_feedbackFilePath);
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
    }
}
