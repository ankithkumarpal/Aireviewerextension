using System;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Represents user feedback on a review result.
    /// Used for the AI learning system to improve over time.
    /// </summary>
    public class ReviewFeedback
    {
        /// <summary>
        /// Unique identifier for tracking this feedback
        /// </summary>
        public string ReviewId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Path of the file that was reviewed
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Line number where the issue was found
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// The issue description that AI provided
        /// </summary>
        public string AiIssueDescription { get; set; } = "";

        /// <summary>
        /// The severity AI assigned (High/Medium/Low)
        /// </summary>
        public string Severity { get; set; } = "Medium";

        /// <summary>
        /// The rule category (Security, Performance, etc.)
        /// </summary>
        public string Rule { get; set; } = "";

        /// <summary>
        /// The actual code snippet that was flagged
        /// </summary>
        public string CodeSnippet { get; set; } = "";

        /// <summary>
        /// Whether the user found this feedback helpful
        /// </summary>
        public bool WasHelpful { get; set; }

        /// <summary>
        /// User's correction if they disagreed with AI
        /// </summary>
        public string UserCorrection { get; set; } = "";

        /// <summary>
        /// User's reason for the feedback
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// When this feedback was provided
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Name of the project being reviewed
        /// </summary>
        public string ProjectName { get; set; } = "";

        /// <summary>
        /// Repository path for context
        /// </summary>
        public string RepositoryPath { get; set; } = "";
    }

    /// <summary>
    /// Container for all feedback data (for JSON serialization)
    /// </summary>
    public class FeedbackData
    {
        /// <summary>
        /// Schema version for backward compatibility
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Total number of reviews that received feedback
        /// </summary>
        public int TotalReviews { get; set; }

        /// <summary>
        /// All feedback entries
        /// </summary>
        public System.Collections.Generic.List<ReviewFeedback> Feedbacks { get; set; } 
            = new System.Collections.Generic.List<ReviewFeedback>();

        /// <summary>
        /// Last time feedback was added
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
