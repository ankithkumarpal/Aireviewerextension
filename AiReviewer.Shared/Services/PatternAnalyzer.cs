using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Retrieves learned patterns from Azure Team Learning API.
    /// Converts patterns into few-shot examples for AI prompt injection.
    /// All data is stored in Azure - no local files.
    /// </summary>
    public class PatternAnalyzer
    {
        private readonly string _repositoryPath;
        private readonly TeamLearningApiClient _teamApiClient;

        /// <summary>
        /// Creates a new PatternAnalyzer with team learning API
        /// </summary>
        public PatternAnalyzer(string repositoryPath, TeamLearningApiClient teamApiClient)
        {
            _repositoryPath = repositoryPath ?? throw new ArgumentNullException(nameof(repositoryPath));
            _teamApiClient = teamApiClient ?? throw new ArgumentNullException(nameof(teamApiClient));
        }

        /// <summary>
        /// Gets relevant patterns from Azure for injection into AI prompt (few-shot learning)
        /// </summary>
        /// <param name="fileExtension">File extension to filter by</param>
        /// <param name="maxPatterns">Maximum patterns to return</param>
        /// <param name="minAccuracy">Minimum accuracy threshold</param>
        public List<FewShotExample> GetRelevantPatterns(
            string fileExtension = ".cs", 
            int maxPatterns = 20, 
            double minAccuracy = 40.0)
        {
            try
            {
                return GetPatternsAsync(fileExtension, maxPatterns, minAccuracy)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Failed to get patterns: {ex.Message}");
                return new List<FewShotExample>();
            }
        }

        /// <summary>
        /// Gets patterns from the team learning API asynchronously
        /// </summary>
        public async Task<List<FewShotExample>> GetPatternsAsync(
            string fileExtension = ".cs",
            int maxPatterns = 20,
            double minAccuracy = 40.0)
        {
            var ext = fileExtension ?? ".cs";
            var response = await _teamApiClient.GetPatternsAsync(ext, 2, maxPatterns, minAccuracy)
                .ConfigureAwait(false);

            if (response == null || response.Patterns == null || response.Patterns.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[AI Reviewer] No patterns returned from Azure");
                return new List<FewShotExample>();
            }

            var examples = new List<FewShotExample>();

            foreach (var pattern in response.Patterns)
            {
                foreach (var example in pattern.Examples)
                {
                    if (example.WasHelpful && pattern.Accuracy >= minAccuracy)
                    {
                        // Positive example - reinforce
                        examples.Add(new FewShotExample
                        {
                            Rule = pattern.Rule,
                            Learning = $"Team found this helpful ({pattern.Accuracy}% accuracy, {pattern.TotalOccurrences} reviews)",
                            CodeExample = example.CodeSnippet,
                            CorrectSuggestion = example.OriginalSuggestion,
                            IsPositiveExample = true,
                            Confidence = GetConfidenceFromAccuracy(pattern.Accuracy)
                        });
                    }
                    else if (!example.WasHelpful && !string.IsNullOrEmpty(example.Correction))
                    {
                        // Negative example with correction
                        examples.Add(new FewShotExample
                        {
                            Rule = pattern.Rule,
                            Learning = $"Team corrected this ({pattern.NotHelpfulCount} times): {example.Reason}",
                            CodeExample = example.CodeSnippet,
                            CorrectSuggestion = example.Correction,
                            IsPositiveExample = false,
                            Confidence = GetConfidenceFromAccuracy(pattern.Accuracy)
                        });
                    }
                    else if (!example.WasHelpful && pattern.Accuracy < minAccuracy)
                    {
                        // Low accuracy pattern - avoid
                        examples.Add(new FewShotExample
                        {
                            Rule = pattern.Rule,
                            Learning = $"AVOID: Team marked unhelpful ({pattern.Accuracy}% accuracy). Reason: {example.Reason ?? "Not relevant"}",
                            CodeExample = example.CodeSnippet,
                            CorrectSuggestion = "Do not flag this pattern unless clearly problematic",
                            IsPositiveExample = false,
                            Confidence = GetConfidenceFromAccuracy(pattern.Accuracy)
                        });
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Retrieved {examples.Count} patterns from Azure");
            return examples.Take(maxPatterns).ToList();
        }

        /// <summary>
        /// Converts accuracy percentage to confidence level string
        /// </summary>
        private string GetConfidenceFromAccuracy(double accuracy)
        {
            if (accuracy >= 90) return "Very High";
            if (accuracy >= 75) return "High";
            if (accuracy >= 50) return "Medium";
            return "Low";
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
            sb.AppendLine("\n## LEARNED FROM TEAM FEEDBACK (CHECKID prefix: team-)");
            sb.AppendLine("When you report an issue based on these learned patterns, use CHECKID: team-{rule} (e.g., team-LOGIC, team-STYLE)");
            sb.AppendLine();

            var positiveExamples = examples.Where(e => e.IsPositiveExample).ToList();
            var negativeExamples = examples.Where(e => !e.IsPositiveExample).ToList();

            if (positiveExamples.Count > 0)
            {
                sb.AppendLine("### ✅ PATTERNS THAT WORK WELL (use CHECKID: team-{rule} when applying):");
                foreach (var ex in positiveExamples.Take(10))
                {
                    sb.AppendLine($"- [team-{ex.Rule}] {ex.Learning}");
                    if (!string.IsNullOrEmpty(ex.CorrectSuggestion))
                    {
                        sb.AppendLine($"  Example: \"{TruncateString(ex.CorrectSuggestion, 150)}\"");
                    }
                }
                sb.AppendLine();
            }

            if (negativeExamples.Count > 0)
            {
                sb.AppendLine("### ⚠️ PATTERNS TO AVOID (learned from negative feedback):");
                foreach (var ex in negativeExamples.Take(10))
                {
                    sb.AppendLine($"- [team-{ex.Rule}] {ex.Learning}");
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
        /// Gets learning statistics from Azure
        /// </summary>
        public async Task<LearningStats> GetLearningStatsAsync()
        {
            var teamStats = await _teamApiClient.GetStatsAsync().ConfigureAwait(false);
            
            if (teamStats == null)
            {
                return new LearningStats();
            }

            return new LearningStats
            {
                TotalPatterns = teamStats.UniquePatterns,
                TotalFeedbackProcessed = teamStats.TotalFeedback,
                OverallAccuracy = teamStats.HelpfulRate,
                LastAnalyzed = DateTime.UtcNow,
                UniqueContributors = teamStats.UniqueContributors,
                FeedbackStats = new FeedbackStats
                {
                    TotalFeedback = teamStats.TotalFeedback,
                    HelpfulCount = teamStats.HelpfulCount,
                    NotHelpfulCount = teamStats.NotHelpfulCount,
                    AccuracyPercent = teamStats.HelpfulRate,
                    IsTeamStats = true,
                    UniqueContributors = teamStats.UniqueContributors,
                    TopContributors = teamStats.TopContributors
                }
            };
        }

        /// <summary>
        /// Gets learning statistics (sync version)
        /// </summary>
        public LearningStats GetLearningStats()
        {
            try
            {
                return GetLearningStatsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Failed to get learning stats: {ex.Message}");
                return new LearningStats();
            }
        }

        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
        }
    }
}
