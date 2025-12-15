using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Represents a set of changes (patch) to a single file.
    /// </summary>
    public class Patch
    {
        public string FilePath { get; }
        public List<Hunk> Hunks { get; }

        public Patch(string filePath, List<Hunk> hunks)
        {
            FilePath = filePath;
            Hunks = hunks;
        }
    }
}