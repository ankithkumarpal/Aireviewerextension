using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    public enum Severity { Info, Warning, Error }

    public sealed class Pattern
    {
        public string Type { get; set; }   // "metrics" | "roslyn-inspect" | "regex" | "list"
        public object Value { get; set; }  // shape depends on Type
    }

    public sealed class Check
    {
        public string Id { get; set; } = "";
        public List<string> AppliesTo { get; set; } = new List<string>();
        public Severity Severity { get; set; } = Severity.Warning;
        public string Description { get; set; } = "";
        public Pattern Pattern { get; set; }
        public string Guidance { get; set; } = "";
        /// <summary>
        /// Scope: "file" (default), "pr", or "both"
        /// </summary>
        public string Scope { get; set; } = "file";
    }

    /// <summary>
    /// PR-level checks (title, description, labels, size, etc.)
    /// </summary>
    public sealed class PrCheck
    {
        public string Id { get; set; } = "";
        public Severity Severity { get; set; } = Severity.Warning;
        public string Description { get; set; } = "";
        public string Guidance { get; set; } = "";
        /// <summary>
        /// Check type: "title_pattern", "description_required", "max_files", "max_lines", "required_labels", "forbidden_labels"
        /// </summary>
        public string Type { get; set; } = "";
        /// <summary>
        /// Value depends on Type (e.g., regex pattern, number, list of labels)
        /// </summary>
        public object Value { get; set; }
    }

    /// <summary>
    /// PR metadata passed for review
    /// </summary>
    public sealed class PrMetadata
    {
        public string PrNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string SourceBranch { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public List<string> Labels { get; set; } = new List<string>();
        public List<string> FilesChanged { get; set; } = new List<string>();
        public int TotalAdditions { get; set; }
        public int TotalDeletions { get; set; }
        public string Url { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result of a PR-level check
    /// </summary>
    public sealed class PrCheckResult
    {
        public string CheckId { get; set; } = "";
        public Severity Severity { get; set; } = Severity.Warning;
        public string Message { get; set; } = "";
        public string Guidance { get; set; } = "";
        public bool Passed { get; set; } = true;
    }

    public sealed class StagebotConfig
    {
        public int Version { get; set; } = 1;
        public List<string> IncludePaths { get; set; } = new List<string>();
        public List<string> ExcludePaths { get; set; } = new List<string>();
        public List<Check> Checks { get; set; } = new List<Check>();
        /// <summary>
        /// PR-level checks (title format, description, size limits, labels)
        /// </summary>
        public List<PrCheck> PrChecks { get; set; } = new List<PrCheck>();
        /// <summary>
        /// If true, inherit checks from central NNF standards (merged with local)
        /// </summary>
        public bool InheritCentralStandards { get; set; } = true;
    }
}
