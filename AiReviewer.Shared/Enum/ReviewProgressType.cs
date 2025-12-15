using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Enum
{
    /// <summary>
    /// Progress update types for streaming review feedback
    /// </summary>
    public enum ReviewProgressType
    {
        Started,
        BuildingPrompt,
        CallingAI,
        Streaming,
        ParsingResults,
        Completed
    }
}
