using Azure;
using Azure.Data.Tables;

namespace AiReviewer.Functions.Models;

/// <summary>
/// Entity stored in Azure Table Storage representing a single feedback item.
/// 
/// Table Design:
/// - PartitionKey = File Extension (.cs, .js, etc.) 
///     Enables fast queries by file type
/// - RowKey = Unique GUID
///     Ensures no conflicts on concurrent inserts
/// </summary>
public class FeedbackEntity : ITableEntity
{
    /// <summary>
    /// File extension (e.g., ".cs", ".js", ".py")
    /// Used as partition key for efficient querying by file type
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this feedback (GUID)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// ETag for optimistic concurrency (managed by Table Storage)
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp when the entity was created (managed by Table Storage)
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// The rule category (LOGIC, STYLE, SECURITY, PERFORMANCE, BEST_PRACTICE)
    /// </summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>
    /// The code snippet that was reviewed
    /// </summary>
    public string CodeSnippet { get; set; } = string.Empty;

    /// <summary>
    /// The AI's original suggestion
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the suggestion for grouping similar issues
    /// </summary>
    public string IssueHash { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user found this helpful
    /// </summary>
    public bool IsHelpful { get; set; }

    /// <summary>
    /// User's reason for marking as not helpful (optional)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// User's correction if they provided one (optional)
    /// </summary>
    public string? Correction { get; set; }

    /// <summary>
    /// Who submitted this feedback (username/alias)
    /// </summary>
    public string Contributor { get; set; } = string.Empty;

    /// <summary>
    /// Repository name for context
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// When the feedback was submitted
    /// </summary>
    public DateTime FeedbackDate { get; set; }
}
