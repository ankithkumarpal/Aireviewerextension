using System;
using System.Collections.Generic;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Request model for submitting feedback to the Team Learning API.
    /// </summary>
    public class TeamFeedbackRequest
    {
        /// <summary>
        /// File extension (e.g., ".cs", ".js")
        /// </summary>
        public string FileExtension { get; set; } = string.Empty;

        /// <summary>
        /// Rule category (LOGIC, STYLE, SECURITY, etc.)
        /// </summary>
        public string Rule { get; set; } = string.Empty;

        /// <summary>
        /// The code that was reviewed
        /// </summary>
        public string CodeSnippet { get; set; } = string.Empty;

        /// <summary>
        /// The AI's suggestion
        /// </summary>
        public string Suggestion { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the suggestion for pattern grouping
        /// </summary>
        public string IssueHash { get; set; } = string.Empty;

        /// <summary>
        /// Was the suggestion helpful?
        /// </summary>
        public bool IsHelpful { get; set; }

        /// <summary>
        /// Why was it not helpful? (optional)
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// User's correction (optional)
        /// </summary>
        public string? Correction { get; set; }

        /// <summary>
        /// Who submitted this feedback
        /// </summary>
        public string Contributor { get; set; } = string.Empty;

        /// <summary>
        /// Repository name
        /// </summary>
        public string Repository { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from GET /api/patterns containing learned patterns
    /// </summary>
    public class TeamPatternsResponse
    {
        /// <summary>
        /// List of learned patterns for the requested file extension
        /// </summary>
        public List<TeamLearnedPattern> Patterns { get; set; } = new List<TeamLearnedPattern>();

        /// <summary>
        /// Total number of feedback items for this extension
        /// </summary>
        public int TotalFeedbackCount { get; set; }
    }

    /// <summary>
    /// A learned pattern from the team
    /// </summary>
    public class TeamLearnedPattern
    {
        /// <summary>
        /// Unique key for this pattern (Rule|Extension|Hash)
        /// </summary>
        public string PatternKey { get; set; } = string.Empty;

        /// <summary>
        /// Rule category
        /// </summary>
        public string Rule { get; set; } = string.Empty;

        /// <summary>
        /// File extension this pattern applies to
        /// </summary>
        public string FileExtension { get; set; } = string.Empty;

        /// <summary>
        /// How many times this pattern was seen
        /// </summary>
        public int TotalOccurrences { get; set; }

        /// <summary>
        /// How many times users found it helpful
        /// </summary>
        public int HelpfulCount { get; set; }

        /// <summary>
        /// How many times users found it not helpful
        /// </summary>
        public int NotHelpfulCount { get; set; }

        /// <summary>
        /// Accuracy percentage (HelpfulCount / TotalOccurrences * 100)
        /// </summary>
        public double Accuracy { get; set; }

        /// <summary>
        /// Example feedback items for few-shot learning
        /// </summary>
        public List<TeamFewShotExample> Examples { get; set; } = new List<TeamFewShotExample>();
    }

    /// <summary>
    /// An example for few-shot learning
    /// </summary>
    public class TeamFewShotExample
    {
        public string CodeSnippet { get; set; } = string.Empty;
        public string OriginalSuggestion { get; set; } = string.Empty;
        public bool WasHelpful { get; set; }
        public string? Correction { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Response from GET /api/stats
    /// </summary>
    public class TeamStatsResponse
    {
        /// <summary>
        /// Total feedback count across all extensions
        /// </summary>
        public int TotalFeedback { get; set; }

        /// <summary>
        /// Number of helpful feedback items
        /// </summary>
        public int HelpfulCount { get; set; }

        /// <summary>
        /// Number of not helpful feedback items
        /// </summary>
        public int NotHelpfulCount { get; set; }

        /// <summary>
        /// Overall helpful rate percentage
        /// </summary>
        public double HelpfulRate { get; set; }

        /// <summary>
        /// Number of unique patterns learned
        /// </summary>
        public int UniquePatterns { get; set; }

        /// <summary>
        /// Number of unique contributors
        /// </summary>
        public int UniqueContributors { get; set; }

        /// <summary>
        /// Breakdown by file extension
        /// </summary>
        public Dictionary<string, int> ByExtension { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Top contributors by feedback count
        /// </summary>
        public List<TeamContributorStat> TopContributors { get; set; } = new List<TeamContributorStat>();
    }

    /// <summary>
    /// Contributor statistics
    /// </summary>
    public class TeamContributorStat
    {
        public string Name { get; set; } = string.Empty;
        public int FeedbackCount { get; set; }
    }
}
