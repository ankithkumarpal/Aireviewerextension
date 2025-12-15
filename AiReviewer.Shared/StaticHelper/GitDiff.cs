// AiReviewer.Shared/Diff.cs
using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AiReviewer.Shared.StaticHelper
{
    /// <summary>
    /// Provides utilities for retrieving and parsing Git diffs.
    /// </summary>
    public static class GitDiff
    {
        /// <summary>
        /// Finds the root directory of the current Git repository.
        /// </summary>
        /// <param name="startDirectory">The directory to start searching from. If null, uses the current directory.</param>
        /// <returns>The absolute path to the repository root, or an empty string if not found.</returns>
        public static string FindRepoRoot(string? startDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --show-toplevel",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
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

        /// <summary>
        /// Gets the staged unified diff for the repository, with extended context lines.
        /// </summary>
        /// <param name="repoRoot">The root directory of the Git repository.</param>
        /// <returns>The unified diff as a string, or an empty string if unavailable.</returns>
        public static string GetStagedUnifiedDiff(string repoRoot)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --cached --unified=30",  // Get 30 lines of context for comprehensive AI understanding
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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
        /// <summary>
        /// Parses a unified diff string and extracts patches and hunks, focusing on added lines and context.
        /// </summary>
        /// <param name="diff">The unified diff string to parse.</param>
        /// <returns>A list of <see cref="Patch"/> objects representing the parsed diff.</returns>
        public static List<Patch> ParseUnified(string diff)
        {
            var patches = new List<Patch>();
            Patch? current = null;
            Hunk? hunk = null;

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

                    // Capture context lines (space prefix), added lines (+), but ignore deletions (-)
                    if (line.StartsWith(" ") || line.StartsWith("+") && !line.StartsWith("+++ "))
                    {
                        hunk?.Lines.Add(line); // Keep the prefix to show context vs additions
                    }
                }
            }

            return patches;
        }
    }
}