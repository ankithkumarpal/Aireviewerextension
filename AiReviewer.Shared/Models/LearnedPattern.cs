using System;
using System.Collections.Generic;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Represents a pattern learned from user feedback.
    /// Used to improve AI suggestions based on past corrections.
    /// </summary>
    public class LearnedPattern
    {
        /// <summary>
        /// Unique identifier for the pattern
        /// </summary>
        public string PatternId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// The rule category this pattern applies to (e.g., "Security", "Performance")
        /// </summary>
        public string Rule { get; set; }

        /// <summary>
        /// Keywords or code patterns that trigger this learning
        /// </summary>
        public List<string> TriggerKeywords { get; set; } = new List<string>();

        /// <summary>
        /// What the AI originally suggested (may have been wrong)
        /// </summary>
        public string OriginalAiSuggestion { get; set; }

        /// <summary>
        /// The user's correction (what should have been suggested)
        /// </summary>
        public string UserCorrection { get; set; }

        /// <summary>
        /// Whether this type of suggestion was helpful overall
        /// </summary>
        public bool WasHelpful { get; set; }

        /// <summary>
        /// Number of times this pattern has been confirmed (helpful count)
        /// </summary>
        public int HelpfulCount { get; set; }

        /// <summary>
        /// Number of times this pattern was marked not helpful
        /// </summary>
        public int NotHelpfulCount { get; set; }

        /// <summary>
        /// Accuracy percentage (HelpfulCount / TotalCount * 100)
        /// </summary>
        public double AccuracyPercent => TotalOccurrences > 0 
            ? Math.Round((double)HelpfulCount / TotalOccurrences * 100, 1) 
            : 0;

        /// <summary>
        /// Total number of times this pattern has occurred
        /// </summary>
        public int TotalOccurrences => HelpfulCount + NotHelpfulCount;

        /// <summary>
        /// Confidence level based on sample size and accuracy
        /// </summary>
        public string ConfidenceLevel => TotalOccurrences >= 10 ? "High" :
                                          TotalOccurrences >= 5 ? "Medium" : "Low";

        /// <summary>
        /// File extension this pattern applies to (e.g., ".cs", ".js")
        /// </summary>
        public string FileExtension { get; set; }

        /// <summary>
        /// Code context snippet that triggered this pattern
        /// </summary>
        public string CodeContext { get; set; }

        /// <summary>
        /// When this pattern was first identified
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this pattern was last updated
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Example of correct code (for few-shot learning)
        /// </summary>
        public string CorrectCodeExample { get; set; }

        /// <summary>
        /// Example of incorrect code (for few-shot learning)
        /// </summary>
        public string IncorrectCodeExample { get; set; }

        /// <summary>
        /// A hash of the pattern key for quick lookups
        /// </summary>
        public string PatternHash { get; set; }
    }

    /// <summary>
    /// Container for all learned patterns
    /// </summary>
    public class LearnedPatternData
    {
        /// <summary>
        /// Version for future migrations
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// All learned patterns
        /// </summary>
        public List<LearnedPattern> Patterns { get; set; } = new List<LearnedPattern>();

        /// <summary>
        /// Total feedback entries processed
        /// </summary>
        public int TotalFeedbackProcessed { get; set; }

        /// <summary>
        /// Overall accuracy across all patterns
        /// </summary>
        public double OverallAccuracy { get; set; }

        /// <summary>
        /// When patterns were last analyzed
        /// </summary>
        public DateTime LastAnalyzed { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A pattern ready to be injected into the AI prompt (few-shot example)
    /// </summary>
    public class FewShotExample
    {
        /// <summary>
        /// The rule category
        /// </summary>
        public string Rule { get; set; }

        /// <summary>
        /// Description of what was learned
        /// </summary>
        public string Learning { get; set; }

        /// <summary>
        /// Example of code that triggers this pattern
        /// </summary>
        public string CodeExample { get; set; }

        /// <summary>
        /// What the AI should suggest (or avoid)
        /// </summary>
        public string CorrectSuggestion { get; set; }

        /// <summary>
        /// Whether this is a "do this" or "don't do this" example
        /// </summary>
        public bool IsPositiveExample { get; set; }

        /// <summary>
        /// Confidence in this learning (based on feedback count)
        /// </summary>
        public string Confidence { get; set; }
    }
}
