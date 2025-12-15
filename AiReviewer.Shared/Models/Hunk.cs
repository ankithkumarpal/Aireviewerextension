using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Represents a contiguous block of changes (hunk) in a diff.
    /// </summary>
    public class Hunk
    {
        public int StartLine { get; }
        public List<string> Lines { get; }

        public Hunk(int startLine, List<string> lines)
        {
            StartLine = startLine;
            Lines = lines;
        }
    }
}
