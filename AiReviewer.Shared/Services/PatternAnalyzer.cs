using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Analyzes user feedback to extract patterns for AI learning.
    /// Converts raw feedback into actionable few-shot examples.
    /// </summary>
    public class PatternAnalyzer
    {
        private static readonly string PatternFileName = "learned-patterns.json";
        private static readonly string ConfigFolder = ".config/ai-reviewer";

        private readonly string _repositoryPath;
        private readonly string _patternFilePath;
        private readonly FeedbackManager _feedbackManager;

        /// <summary>
        /// Creates a new PatternAnalyzer for the specified repository
        /// </summary>
        public PatternAnalyzer(string repositoryPath)
        {
            _repositoryPath = repositoryPath;
            _patternFilePath = Path.Combine(repositoryPath, ConfigFolder, PatternFileName);
            _feedbackManager = new FeedbackManager(repositoryPath);
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_patternFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Analyzes all feedback and extracts/updates patterns
        /// </summary>
        public LearnedPatternData AnalyzeFeedback()
        {
            var feedbackData = _feedbackManager.LoadFeedback();
            var existingPatterns = LoadPatterns();

            if (feedbackData.Feedbacks.Count == 0)
            {
                return existingPatterns;
            }

            // Group feedback by rule and similar issues
            var groupedFeedback = GroupFeedbackByPattern(feedbackData.Feedbacks);

            // Convert grouped feedback to patterns
            foreach (var group in groupedFeedback)
            {
                var pattern = CreateOrUpdatePattern(existingPatterns, group.Key, group.Value);
                if (pattern != null)
                {
                    var existingIndex = existingPatterns.Patterns
                        .FindIndex(p => p.PatternHash == pattern.PatternHash);

                    if (existingIndex >= 0)
                    {
                        existingPatterns.Patterns[existingIndex] = pattern;
                    }
                    else
                    {
                        existingPatterns.Patterns.Add(pattern);
                    }
                }
            }

            // Calculate overall accuracy
            if (existingPatterns.Patterns.Count > 0)
            {
                var totalHelpful = existingPatterns.Patterns.Sum(p => p.HelpfulCount);
                var totalOccurrences = existingPatterns.Patterns.Sum(p => p.TotalOccurrences);
                existingPatterns.OverallAccuracy = totalOccurrences > 0
                    ? Math.Round((double)totalHelpful / totalOccurrences * 100, 1)
                    : 0;
            }

            existingPatterns.TotalFeedbackProcessed = feedbackData.Feedbacks.Count;
            existingPatterns.LastAnalyzed = DateTime.UtcNow;

            // Save updated patterns
            SavePatterns(existingPatterns);

            System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Analyzed {feedbackData.Feedbacks.Count} feedback entries, found {existingPatterns.Patterns.Count} patterns");

            return existingPatterns;
        }

        /// <summary>
        /// Groups feedback by similar patterns (rule + issue type)
        /// </summary>
        private Dictionary<string, List<ReviewFeedback>> GroupFeedbackByPattern(List<ReviewFeedback> feedbacks)
        {
            var groups = new Dictionary<string, List<ReviewFeedback>>();

            foreach (var feedback in feedbacks)
            {
                // Create a pattern key based on rule and normalized issue description
                var patternKey = GeneratePatternKey(feedback);

                if (!groups.ContainsKey(patternKey))
                {
                    groups[patternKey] = new List<ReviewFeedback>();
                }
                groups[patternKey].Add(feedback);
            }

            return groups;
        }

        /// <summary>
        /// Generates a unique key for grouping similar feedback
        /// </summary>
        private string GeneratePatternKey(ReviewFeedback feedback)
        {
            // Normalize the issue description by extracting key terms
            var normalizedIssue = NormalizeIssueDescription(feedback.AiIssueDescription);
            var fileExt = GetFileExtension(feedback.FilePath);

            return $"{feedback.Rule}|{fileExt}|{normalizedIssue}";
        }

        /// <summary>
        /// Normalizes issue description for pattern matching
        /// </summary>
        private string NormalizeIssueDescription(string issue)
        {
            if (string.IsNullOrEmpty(issue)) return "unknown";

            // Extract key phrases, remove variable names and specific details
            var normalized = issue.ToLowerInvariant();

            // Remove common variable patterns
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"'[^']*'", "VAR");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"`[^`]*`", "VAR");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\b\d+\b", "NUM");

            // Keep only first 100 chars for matching
            if (normalized.Length > 100)
                normalized = normalized.Substring(0, 100);

            return ComputeHash(normalized);
        }

        /// <summary>
        /// Creates or updates a pattern from grouped feedback
        /// </summary>
        private LearnedPattern CreateOrUpdatePattern(LearnedPatternData existing, string patternKey, List<ReviewFeedback> feedbacks)
        {
            if (feedbacks.Count == 0) return null;

            var patternHash = ComputeHash(patternKey);
            var existingPattern = existing.Patterns.FirstOrDefault(p => p.PatternHash == patternHash);

            var pattern = existingPattern ?? new LearnedPattern
            {
                PatternHash = patternHash,
                CreatedAt = DateTime.UtcNow
            };

            // Use the first feedback as the representative example
            var representative = feedbacks.First();
            pattern.Rule = representative.Rule;
            pattern.FileExtension = GetFileExtension(representative.FilePath);
            pattern.OriginalAiSuggestion = representative.AiIssueDescription;
            pattern.CodeContext = representative.CodeSnippet;

            // Count helpful vs not helpful
            var helpful = feedbacks.Count(f => f.WasHelpful);
            var notHelpful = feedbacks.Count(f => !f.WasHelpful);

            pattern.HelpfulCount = helpful;
            pattern.NotHelpfulCount = notHelpful;
            pattern.WasHelpful = helpful > notHelpful;

            // Get the best user correction (if any)
            var correction = feedbacks
                .Where(f => !string.IsNullOrEmpty(f.UserCorrection))
                .OrderByDescending(f => f.Timestamp)
                .FirstOrDefault();

            if (correction != null)
            {
                pattern.UserCorrection = correction.UserCorrection;
                pattern.CorrectCodeExample = correction.UserCorrection;
                pattern.IncorrectCodeExample = correction.CodeSnippet;
            }

            // Extract trigger keywords from code snippets
            pattern.TriggerKeywords = ExtractKeywords(feedbacks);

            pattern.LastUpdated = DateTime.UtcNow;

            return pattern;
        }

        /// <summary>
        /// Extracts common keywords from code snippets
        /// </summary>
        private List<string> ExtractKeywords(List<ReviewFeedback> feedbacks)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var feedback in feedbacks)
            {
                if (string.IsNullOrEmpty(feedback.CodeSnippet)) continue;

                // Extract common C# keywords and patterns
                var codeKeywords = new[] { "async", "await", "null", "throw", "try", "catch", 
                    "using", "dispose", "lock", "static", "public", "private", "protected",
                    "virtual", "override", "abstract", "interface", "class", "struct",
                    "Task", "IDisposable", "IEnumerable", "List", "Dictionary", "var" };

                var snippetLower = feedback.CodeSnippet.ToLowerInvariant();
                foreach (var kw in codeKeywords)
                {
                    if (snippetLower.Contains(kw.ToLowerInvariant()))
                    {
                        keywords.Add(kw);
                    }
                }
            }

            return keywords.Take(10).ToList();
        }

        /// <summary>
        /// Gets file extension from path
        /// </summary>
        private string GetFileExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return ".cs";
            return Path.GetExtension(filePath)?.ToLowerInvariant() ?? ".cs";
        }

        /// <summary>
        /// Computes a short hash for pattern matching
        /// </summary>
        private string ComputeHash(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        /// <summary>
        /// Loads existing patterns from file
        /// </summary>
        public LearnedPatternData LoadPatterns()
        {
            try
            {
                if (!File.Exists(_patternFilePath))
                {
                    return new LearnedPatternData();
                }

                var json = File.ReadAllText(_patternFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<LearnedPatternData>(json, options) ?? new LearnedPatternData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading patterns: {ex.Message}");
                return new LearnedPatternData();
            }
        }

        /// <summary>
        /// Saves patterns to file
        /// </summary>
        public void SavePatterns(LearnedPatternData data)
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
                File.WriteAllText(_patternFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving patterns: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets relevant patterns for injection into AI prompt (few-shot learning)
        /// Returns top patterns filtered by accuracy and relevance
        /// </summary>
        /// <param name="fileExtension">File extension to filter by (optional)</param>
        /// <param name="maxPatterns">Maximum patterns to return (default 20)</param>
        /// <param name="minAccuracy">Minimum accuracy threshold (default 40%)</param>
        public List<FewShotExample> GetRelevantPatterns(
            string fileExtension = null, 
            int maxPatterns = 20, 
            double minAccuracy = 40.0)
        {
            var patterns = LoadPatterns();
            var examples = new List<FewShotExample>();

            // Filter and sort patterns
            var relevantPatterns = patterns.Patterns
                .Where(p => p.TotalOccurrences >= 2) // At least 2 data points
                .Where(p => string.IsNullOrEmpty(fileExtension) || 
                           p.FileExtension == fileExtension || 
                           p.FileExtension == ".cs") // Include C# as fallback
                .OrderByDescending(p => p.TotalOccurrences) // Prioritize by sample size
                .ThenByDescending(p => p.AccuracyPercent)
                .Take(maxPatterns)
                .ToList();

            foreach (var pattern in relevantPatterns)
            {
                // Create a few-shot example based on pattern type
                if (pattern.WasHelpful && pattern.AccuracyPercent >= minAccuracy)
                {
                    // This type of suggestion works well - reinforce it
                    examples.Add(new FewShotExample
                    {
                        Rule = pattern.Rule,
                        Learning = $"Users found this type of suggestion helpful ({pattern.AccuracyPercent}% accuracy)",
                        CodeExample = pattern.CodeContext,
                        CorrectSuggestion = pattern.OriginalAiSuggestion,
                        IsPositiveExample = true,
                        Confidence = pattern.ConfidenceLevel
                    });
                }
                else if (!pattern.WasHelpful && !string.IsNullOrEmpty(pattern.UserCorrection))
                {
                    // This suggestion was wrong - teach the correct approach
                    examples.Add(new FewShotExample
                    {
                        Rule = pattern.Rule,
                        Learning = $"Users corrected this type of suggestion ({pattern.NotHelpfulCount} corrections)",
                        CodeExample = pattern.IncorrectCodeExample ?? pattern.CodeContext,
                        CorrectSuggestion = pattern.UserCorrection,
                        IsPositiveExample = false,
                        Confidence = pattern.ConfidenceLevel
                    });
                }
                else if (!pattern.WasHelpful && pattern.AccuracyPercent < minAccuracy)
                {
                    // This type of suggestion is often wrong - avoid it
                    examples.Add(new FewShotExample
                    {
                        Rule = pattern.Rule,
                        Learning = $"AVOID: This suggestion type has low accuracy ({pattern.AccuracyPercent}%)",
                        CodeExample = pattern.CodeContext,
                        CorrectSuggestion = "Do not flag this pattern unless clearly problematic",
                        IsPositiveExample = false,
                        Confidence = pattern.ConfidenceLevel
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Generated {examples.Count} few-shot examples from {relevantPatterns.Count} patterns");

            return examples;
        }

        /// <summary>
        /// Formats few-shot examples for injection into the AI prompt
        /// </summary>
        public string FormatExamplesForPrompt(List<FewShotExample> examples)
        {
            if (examples == null || examples.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("\n## LEARNED FROM PAST REVIEWS (use this to improve your suggestions):");
            sb.AppendLine();

            // Group by positive/negative examples
            var positiveExamples = examples.Where(e => e.IsPositiveExample).ToList();
            var negativeExamples = examples.Where(e => !e.IsPositiveExample).ToList();

            if (positiveExamples.Count > 0)
            {
                sb.AppendLine("### ✅ SUGGESTIONS THAT WORK WELL:");
                foreach (var ex in positiveExamples.Take(10))
                {
                    sb.AppendLine($"- [{ex.Rule}] {ex.Learning}");
                    if (!string.IsNullOrEmpty(ex.CorrectSuggestion))
                    {
                        sb.AppendLine($"  Example: \"{TruncateString(ex.CorrectSuggestion, 150)}\"");
                    }
                }
                sb.AppendLine();
            }

            if (negativeExamples.Count > 0)
            {
                sb.AppendLine("### ⚠️ SUGGESTIONS TO AVOID OR IMPROVE:");
                foreach (var ex in negativeExamples.Take(10))
                {
                    sb.AppendLine($"- [{ex.Rule}] {ex.Learning}");
                    if (!string.IsNullOrEmpty(ex.CorrectSuggestion))
                    {
                        sb.AppendLine($"  Better approach: \"{TruncateString(ex.CorrectSuggestion, 150)}\"");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets learning statistics for display
        /// </summary>
        public LearningStats GetLearningStats()
        {
            var patterns = LoadPatterns();
            var feedbackStats = _feedbackManager.GetStats();

            return new LearningStats
            {
                TotalPatterns = patterns.Patterns.Count,
                HighConfidencePatterns = patterns.Patterns.Count(p => p.ConfidenceLevel == "High"),
                OverallAccuracy = patterns.OverallAccuracy,
                TotalFeedbackProcessed = patterns.TotalFeedbackProcessed,
                LastAnalyzed = patterns.LastAnalyzed,
                FeedbackStats = feedbackStats,
                TopRules = patterns.Patterns
                    .GroupBy(p => p.Rule)
                    .Select(g => new RuleStat { Rule = g.Key, Count = g.Count() })
                    .OrderByDescending(r => r.Count)
                    .Take(5)
                    .ToList()
            };
        }

        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
        }
    }

    /// <summary>
    /// Statistics about the learning system
    /// </summary>
    public class LearningStats
    {
        public int TotalPatterns { get; set; }
        public int HighConfidencePatterns { get; set; }
        public double OverallAccuracy { get; set; }
        public int TotalFeedbackProcessed { get; set; }
        public DateTime LastAnalyzed { get; set; }
        public FeedbackStats FeedbackStats { get; set; }
        public List<RuleStat> TopRules { get; set; } = new List<RuleStat>();
    }

    /// <summary>
    /// Statistics per rule category
    /// </summary>
    public class RuleStat
    {
        public string Rule { get; set; }
        public int Count { get; set; }
    }
}
