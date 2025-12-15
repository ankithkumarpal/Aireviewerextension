using AiReviewer.Shared.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{

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
        public int UniqueContributors { get; set; }
        public FeedbackStats FeedbackStats { get; set; } = new FeedbackStats();
        public List<RuleStat> TopRules { get; set; } = new List<RuleStat>();
    }

}
