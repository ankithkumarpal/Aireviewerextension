// AiReviewer.Shared/Diff.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AiReviewer.Shared
{
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

    public static class GitDiff
    {
        public static string FindRepoRoot(string startDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --show-toplevel",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = startDirectory ?? Directory.GetCurrentDirectory()
                };
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    return string.IsNullOrWhiteSpace(output) ? string.Empty : output;
                }
            }
            catch { return string.Empty; }
        }

        public static string GetStagedUnifiedDiff(string repoRoot)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --cached --unified=0",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return string.Empty;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output;
            }
        }

        // Minimal unified diff parser: collects *added* lines (+) with their starting line
        public static List<Patch> ParseUnified(string diff)
        {
            var patches = new List<Patch>();
            Patch current = null;
            Hunk hunk = null;

            using (var reader = new StringReader(diff))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("+++ b/"))
                    {
                        current = new Patch(line.Substring(6).Trim(), new List<Hunk>());
                        patches.Add(current);
                        hunk = null;
                        continue;
                    }

                    if (line.StartsWith("@@"))
                    {
                        // Example: @@ -12,0 +12,3 @@
                        // We take the +12 start for added lines
                        var plusPart = line.Split('+')[1];
                        var startStr = plusPart.Split(',')[0].Trim();
                        var start = int.TryParse(startStr, out var s) ? s : 1;
                        hunk = new Hunk(start, new List<string>());
                        current?.Hunks.Add(hunk);
                        continue;
                    }

                    // Only capture added content (ignore context and deletions)
                    if (line.StartsWith("+") && !line.StartsWith("+++ "))
                    {
                        hunk?.Lines.Add(line.Substring(1)); // added line content
                    }
                }
            }

            return patches;
        }
    }
}