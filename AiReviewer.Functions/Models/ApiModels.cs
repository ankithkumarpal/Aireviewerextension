namespace AiReviewer.Functions.Models;

/// <summary>
/// Request body for submitting feedback via POST /api/feedback
/// </summary>
public class FeedbackRequest
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
public class PatternsResponse
{
    /// <summary>
    /// List of learned patterns for the requested file extension
    /// </summary>
    public List<LearnedPatternDto> Patterns { get; set; } = new();

    /// <summary>
    /// Total number of feedback items for this extension
    /// </summary>
    public int TotalFeedbackCount { get; set; }
}

/// <summary>
/// A learned pattern with accuracy and examples
/// </summary>
public class LearnedPatternDto
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
    public List<FewShotExampleDto> Examples { get; set; } = new();
}

/// <summary>
///  Example for few-shot learning
/// </summary>
public class FewShotExampleDto
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
public class StatsResponse
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
    public Dictionary<string, int> ByExtension { get; set; } = new();

    /// <summary>
    /// Top contributors by feedback count
    /// </summary>
    public List<ContributorStat> TopContributors { get; set; } = new();
}

/// <summary>
/// Contributor statistics
/// </summary>
public class ContributorStat
{
    public string Name { get; set; } = string.Empty;
    public int FeedbackCount { get; set; }
}
