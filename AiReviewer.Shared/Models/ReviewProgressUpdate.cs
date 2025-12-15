using AiReviewer.Shared.Enum;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Progress update data for review streaming
    /// </summary>
    public class ReviewProgressUpdate
    {
        public ReviewProgressType Type { get; set; }
        public string Message { get; set; } = "";
        public string PartialResponse { get; set; } = "";
        public List<ReviewResult> PartialResults { get; set; } = new List<ReviewResult>();
        public int TotalFiles { get; set; }
        public int ProcessedTokens { get; set; }
    }
}
