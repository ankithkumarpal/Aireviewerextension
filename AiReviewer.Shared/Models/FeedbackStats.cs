using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Statistics about collected feedback
    /// </summary>
    public class FeedbackStats
    {
        public int TotalFeedback { get; set; }
        public int HelpfulCount { get; set; }
        public int NotHelpfulCount { get; set; }
        public double AccuracyPercent { get; set; }
        public int TotalReviews { get; set; }
        public DateTime LastUpdated { get; set; }
        public bool IsTeamStats { get; set; } = true;
        public int UniqueContributors { get; set; }
        public List<TeamContributorStat>? TopContributors { get; set; }
    }
}
