// AiReviewer.Shared/StagedLineService.cs
using AiReviewer.Shared.Models;
using System.Collections.Generic;

namespace AiReviewer.Shared
{
    /// Maps file → added line numbers from staged diff; lets analyzers
    /// decide whether a diagnostic intersects staged lines.
    public sealed class StagedLineService
    {
        private readonly Dictionary<string, HashSet<int>> _fileToAddedLines = new Dictionary<string, HashSet<int>>();

        public StagedLineService(List<Patch> patches)
        {
            foreach (var p in patches)
            {
                var key = Normalize(p.FilePath);
                if (!_fileToAddedLines.TryGetValue(key, out var set))
                {
                    set = new HashSet<int>();
                    _fileToAddedLines[key] = set;
                }

                foreach (var h in p.Hunks)
                {
                    var lineNo = h.StartLine;
                    foreach (var _ in h.Lines)
                    {
                        set.Add(lineNo);
                        lineNo++;
                    }
                }
            }
        }

        public bool Intersects(string filePath, int startLineInclusive, int endLineInclusive)
        {
            var key = Normalize(filePath);
            if (!_fileToAddedLines.TryGetValue(key, out var set)) return false;

            for (int l = startLineInclusive; l <= endLineInclusive; l++)
                if (set.Contains(l)) return true;

            return false;
        }

        private static string Normalize(string path) => path.Replace('\\', '/');
    }

    /// Simple singleton holder so analyzers can query the service.
    public static class StagedServices
    {
        private static StagedLineService _instance;
        public static StagedLineService Instance =>
            _instance ?? throw new System.InvalidOperationException("StagedLineService not initialized.");

        public static void Initialize(List<Patch> patches) => _instance = new StagedLineService(patches);
    }
}
