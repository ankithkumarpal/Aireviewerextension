using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AiReviewer.Shared.Prompts
{
    /// <summary>
    /// Provides filtered checklists based on file types being reviewed
    /// </summary>
    public static class ChecklistProvider
    {
        /// <summary>
        /// Get checks relevant to the files being reviewed
        /// </summary>
        public static List<Check> GetRelevantChecks(StagebotConfig config, IEnumerable<string> filePaths)
        {
            if (config?.Checks == null || !config.Checks.Any())
                return new List<Check>();

            // Get unique file extensions
            var extensions = filePaths
                .Select(f => Path.GetExtension(f)?.ToLowerInvariant())
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .ToList();

            // Filter checks that apply to these extensions
            return config.Checks
                .Where(c => c.AppliesTo == null || 
                           !c.AppliesTo.Any() || 
                           c.AppliesTo.Any(ext => extensions.Contains(ext.ToLowerInvariant())))
                .ToList();
        }

        /// <summary>
        /// Build the dynamic user prompt with relevant checks only
        /// Now includes smart file context for better AI understanding
        /// </summary>
        public static string BuildUserPrompt(
            List<Patch> patches, 
            StagebotConfig config,
            string repositoryPath = null,
            string additionalContext = null)
        {
            var sb = new StringBuilder();

            // Get file types being reviewed
            var filePaths = patches.Select(p => p.FilePath).ToList();
            var fileTypes = string.Join(", ", filePaths.Select(f => Path.GetExtension(f)).Distinct());

            sb.AppendLine($"## FILES TO REVIEW ({patches.Count} files)");
            sb.AppendLine($"File types: {fileTypes}");
            sb.AppendLine();

            // Add relevant checks - grouped by source for clear CHECKID prefixing
            var relevantChecks = GetRelevantChecks(config, filePaths);
            if (relevantChecks.Any())
            {
                // Separate checks by source based on ID prefix
                var repoChecks = relevantChecks.Where(c => c.Id.StartsWith("repo-", StringComparison.OrdinalIgnoreCase)).ToList();
                var nnfChecks = relevantChecks.Where(c => c.Id.StartsWith("nnf-", StringComparison.OrdinalIgnoreCase)).ToList();
                var teamChecks = relevantChecks.Where(c => c.Id.StartsWith("team-", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherChecks = relevantChecks.Where(c => 
                    !c.Id.StartsWith("repo-", StringComparison.OrdinalIgnoreCase) && 
                    !c.Id.StartsWith("nnf-", StringComparison.OrdinalIgnoreCase) &&
                    !c.Id.StartsWith("team-", StringComparison.OrdinalIgnoreCase)).ToList();

                sb.AppendLine("## CODING STANDARDS TO ENFORCE");
                sb.AppendLine();
                sb.AppendLine("⚠️ IMPORTANT: Use the check ID exactly as shown in CHECKID field. The prefix determines the badge:");
                sb.AppendLine("- `repo-*` IDs → CHECKID: repo-xxx (📁 Repo Rule badge)");
                sb.AppendLine("- `nnf-*` IDs → CHECKID: nnf-xxx (📘 NNF Standard badge)");
                sb.AppendLine("- `team-*` IDs → CHECKID: team-xxx (📚 Team Learning badge)");
                sb.AppendLine();

                // Repository-specific checks (highest priority)
                if (repoChecks.Any())
                {
                    sb.AppendLine("### 📁 REPOSITORY RULES (Highest Priority - use repo- prefix in CHECKID)");
                    foreach (var check in repoChecks)
                    {
                        string severityEmoji = check.Severity == Severity.Error ? "🔴" : check.Severity == Severity.Warning ? "🟡" : "🔵";
                        sb.AppendLine($"- {severityEmoji} **{check.Id}** [{check.Severity}]: {check.Description}");
                        if (!string.IsNullOrEmpty(check.Guidance))
                            sb.AppendLine($"  → {check.Guidance}");
                    }
                    sb.AppendLine();
                }

                // NNF standards
                if (nnfChecks.Any())
                {
                    sb.AppendLine("### 📘 NNF STANDARDS (use nnf- prefix in CHECKID)");
                    foreach (var check in nnfChecks)
                    {
                        string severityEmoji = check.Severity == Severity.Error ? "🔴" : check.Severity == Severity.Warning ? "🟡" : "🔵";
                        sb.AppendLine($"- {severityEmoji} **{check.Id}** [{check.Severity}]: {check.Description}");
                        if (!string.IsNullOrEmpty(check.Guidance))
                            sb.AppendLine($"  → {check.Guidance}");
                    }
                    sb.AppendLine();
                }

                // Team learning patterns
                if (teamChecks.Any())
                {
                    sb.AppendLine("### 📚 TEAM LEARNING (use team- prefix in CHECKID)");
                    foreach (var check in teamChecks)
                    {
                        string severityEmoji = check.Severity == Severity.Error ? "🔴" : check.Severity == Severity.Warning ? "🟡" : "🔵";
                        sb.AppendLine($"- {severityEmoji} **{check.Id}** [{check.Severity}]: {check.Description}");
                        if (!string.IsNullOrEmpty(check.Guidance))
                            sb.AppendLine($"  → {check.Guidance}");
                    }
                    sb.AppendLine();
                }

                // Other checks (no prefix - will show as AI Detection)
                if (otherChecks.Any())
                {
                    sb.AppendLine("### 🤖 OTHER CHECKS (use 'none' in CHECKID for AI Detection)");
                    foreach (var check in otherChecks)
                    {
                        string severityEmoji = check.Severity == Severity.Error ? "🔴" : check.Severity == Severity.Warning ? "🟡" : "🔵";
                        sb.AppendLine($"- {severityEmoji} **{check.Id}** [{check.Severity}]: {check.Description}");
                        if (!string.IsNullOrEmpty(check.Guidance))
                            sb.AppendLine($"  → {check.Guidance}");
                    }
                    sb.AppendLine();
                }
            }

            // Add additional context if provided (highest priority - user's explicit ask)
            if (!string.IsNullOrEmpty(additionalContext))
            {
                sb.AppendLine("## USER'S SPECIFIC REQUEST (HIGHEST PRIORITY)");
                sb.AppendLine("⚠️ The following is the user's explicit request. This takes PRECEDENCE over all other checks.");
                sb.AppendLine(additionalContext);
                sb.AppendLine();
            }

            // Add precedence instructions
            sb.AppendLine("## REVIEW PRIORITY ORDER");
            sb.AppendLine("When reviewing, apply rules in this order of precedence (highest to lowest):");
            sb.AppendLine("1. **User's Specific Request** (if provided above) - ALWAYS prioritize what the user explicitly asked");
            sb.AppendLine("2. **Repository Config** - Team-specific rules override organization standards");
            sb.AppendLine("3. **NNF Coding Standards** - Organization-wide best practices");
            sb.AppendLine();
            sb.AppendLine("If there's a conflict between rules, follow the higher priority source.");
            sb.AppendLine();

            // Add file context and changes for each file
            sb.AppendLine("## CODE CONTEXT AND CHANGES");
            sb.AppendLine();
            
            foreach (var patch in patches)
            {
                sb.AppendLine($"=== File: {patch.FilePath} ===");
                sb.AppendLine();
                
                // Try to include file context for better AI understanding
                bool contextAdded = false;
                if (!string.IsNullOrEmpty(repositoryPath))
                {
                    var fullFilePath = Path.Combine(repositoryPath, patch.FilePath.Replace("/", "\\"));
                    if (File.Exists(fullFilePath))
                    {
                        try
                        {
                            contextAdded = AddFileContext(sb, fullFilePath, patch);
                        }
                        catch 
                        { 
                            System.Diagnostics.Debug.WriteLine($"[ChecklistProvider] Could not read file: {fullFilePath}");
                        }
                    }
                }
                
                // Always add the changed lines section
                sb.AppendLine("CHANGED LINES (review these specifically):");
                foreach (var hunk in patch.Hunks)
                {
                    sb.AppendLine($"Starting at line {hunk.StartLine}:");
                    int lineNum = hunk.StartLine;
                    foreach (var line in hunk.Lines ?? new List<string>())
                    {
                        // Show context lines and additions with line numbers
                        if (line.StartsWith("+"))
                        {
                            sb.AppendLine($"{lineNum}: + {line.Substring(1)}  ← NEW LINE");
                        }
                        else if (line.StartsWith(" "))
                        {
                            sb.AppendLine($"{lineNum}:   {line.Substring(1)}  (context)");
                        }
                        
                        // Only increment line number for context and additions (not deletions)
                        if (line.StartsWith(" ") || line.StartsWith("+"))
                            lineNum++;
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
        
        /// <summary>
        /// Add smart file context to the prompt
        /// - Small files (≤1000 lines): Include full file
        /// - Large files (>1000 lines): Include first 200 lines + 50 lines around each change
        /// </summary>
        private static bool AddFileContext(StringBuilder sb, string fullFilePath, Patch patch)
        {
            var fileContent = File.ReadAllText(fullFilePath);
            var lines = fileContent.Split('\n');
            
            // Get the changed line numbers from hunks
            var changedLines = patch.Hunks.SelectMany(h => 
            {
                var result = new List<int>();
                int lineNum = h.StartLine;
                foreach (var l in h.Lines ?? new List<string>())
                {
                    if (l.StartsWith("+")) result.Add(lineNum);
                    if (l.StartsWith(" ") || l.StartsWith("+")) lineNum++;
                }
                return result;
            }).ToList();
            
            if (lines.Length <= 1000 && fileContent.Length <= 40000)
            {
                // Small file: include everything with line numbers
                sb.AppendLine("FULL FILE CONTEXT (for better understanding):");
                sb.AppendLine("```");
                int lineNum = 1;
                foreach (var line in lines)
                {
                    var marker = changedLines.Contains(lineNum) ? " ←CHANGED" : "";
                    sb.AppendLine($"{lineNum,4}: {line.TrimEnd('\r')}{marker}");
                    lineNum++;
                }
                sb.AppendLine("```");
                sb.AppendLine();
                return true;
            }
            else
            {
                // Large file: include smart context (header + separate windows around changes)
                sb.AppendLine($"PARTIAL FILE CONTEXT (file has {lines.Length} lines, showing relevant sections):");
                sb.AppendLine("```");
                
                // Always include first 200 lines (imports, class declaration, fields, constructor)
                int headerLines = Math.Min(200, lines.Length);
                for (int i = 0; i < headerLines; i++)
                {
                    var marker = changedLines.Contains(i + 1) ? " ←CHANGED" : "";
                    sb.AppendLine($"{i + 1,4}: {lines[i].TrimEnd('\r')}{marker}");
                }
                
                // Build separate context windows for each cluster of changes
                // Changes within 100 lines of each other are merged into one window
                if (changedLines.Any())
                {
                    var sortedChanges = changedLines.OrderBy(x => x).ToList();
                    var contextWindows = new List<(int Start, int End, List<int> Changes)>();
                    
                    // Group changes into clusters (merge if within 100 lines)
                    int windowStart = sortedChanges[0];
                    int windowEnd = sortedChanges[0];
                    var windowChanges = new List<int> { sortedChanges[0] };
                    
                    for (int i = 1; i < sortedChanges.Count; i++)
                    {
                        if (sortedChanges[i] - windowEnd <= 100)
                        {
                            // Merge into current window
                            windowEnd = sortedChanges[i];
                            windowChanges.Add(sortedChanges[i]);
                        }
                        else
                        {
                            // Save current window, start new one
                            contextWindows.Add((windowStart, windowEnd, new List<int>(windowChanges)));
                            windowStart = sortedChanges[i];
                            windowEnd = sortedChanges[i];
                            windowChanges = new List<int> { sortedChanges[i] };
                        }
                    }
                    // Add the last window
                    contextWindows.Add((windowStart, windowEnd, windowChanges));
                    
                    int lastEndLine = headerLines;
                    
                    foreach (var window in contextWindows)
                    {
                        // Skip if window is within the header we already showed
                        if (window.End <= headerLines)
                            continue;
                            
                        int contextStart = Math.Max(lastEndLine + 1, window.Start - 50);
                        int contextEnd = Math.Min(lines.Length, window.End + 50);
                        
                        // Show omitted lines indicator
                        if (contextStart > lastEndLine + 1)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"...[lines {lastEndLine + 1}-{contextStart - 1} omitted] ...");
                            sb.AppendLine();
                        }
                        
                        // Output context window
                        for (int i = contextStart - 1; i < contextEnd && i < lines.Length; i++)
                        {
                            var marker = window.Changes.Contains(i + 1) ? " ←CHANGED" : "";
                            sb.AppendLine($"{i + 1,4}: {lines[i].TrimEnd('\r')}{marker}");
                        }
                        
                        lastEndLine = contextEnd;
                    }
                    
                    // Show trailing omitted lines
                    if (lastEndLine < lines.Length)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"...[lines {lastEndLine + 1}-{lines.Length} omitted] ...");
                    }
                }
                else if (lines.Length > headerLines)
                {
                    sb.AppendLine();
                    sb.AppendLine($"...[lines {headerLines + 1}-{lines.Length} omitted] ...");
                }
                
                sb.AppendLine("```");
                sb.AppendLine();
                return true;
            }
        }

        /// <summary>
        /// Estimate token count for the prompt (rough estimate: 1 token ≈ 4 chars)
        /// </summary>
        public static int EstimateTokens(string text)
        {
            return (text?.Length ?? 0) / 4;
        }

        /// <summary>
        /// Build a compact user prompt when we're near token limits
        /// </summary>
        public static string BuildCompactUserPrompt(List<Patch> patches, StagebotConfig config)
        {
            var sb = new StringBuilder();
            
            // Minimal header
            sb.AppendLine("Review these changes:");
            sb.AppendLine();

            // Only include most critical checks
            var criticalChecks = config?.Checks?
                .Where(c => c.Severity == Severity.Error)
                .Take(5)
                .ToList();

            if (criticalChecks?.Any() == true)
            {
                sb.AppendLine("Critical checks:");
                foreach (var check in criticalChecks)
                {
                    sb.AppendLine($"- {check.Id}: {check.Description}");
                }
                sb.AppendLine();
            }

            // Compact diff
            sb.AppendLine("```diff");
            foreach (var patch in patches)
            {
                sb.AppendLine($"--- {patch.FilePath}");
                foreach (var hunk in patch.Hunks)
                {
                    var lineCount = hunk.Lines?.Count ?? 0;
                    // Only include added lines to save space
                    sb.AppendLine($"@@ +{hunk.StartLine},{lineCount} @@");
                    foreach (var line in (hunk.Lines ?? new List<string>()).Where(l => l.StartsWith("+") || l.StartsWith("@@")))
                    {
                        sb.AppendLine(line);
                    }
                }
            }
            sb.AppendLine("```");

            return sb.ToString();
        }
    }
}
