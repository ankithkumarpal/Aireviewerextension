using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    public class ReviewResult
    {
        /// <summary>
        /// Unique ID for tracking feedback on this review
        /// </summary>
        public string ReviewId { get; set; } = Guid.NewGuid().ToString("N");

        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string Severity { get; set; } = "Medium";
        public string Confidence { get; set; } = "Medium";
        public string Issue { get; set; } = "";
        public string Suggestion { get; set; } = "";
        public string Rule { get; set; } = "";
        
        /// <summary>
        /// The specific check ID if matched (e.g., "nnf-async-002", "repo-sec-001")
        /// </summary>
        public string CheckId { get; set; } = "";
        
        /// <summary>
        /// Source of the rule: "NNF" (org standard), "Repo" (repo config), "AI" (general AI detection)
        /// </summary>
        public string RuleSource { get; set; } = "AI";
        
        public string CodeSnippet { get; set; } = "";
        public string FixedCode { get; set; } = "";
        public string RepositoryPath { get; set; } = "";

        /// <summary>
        /// When this review was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
