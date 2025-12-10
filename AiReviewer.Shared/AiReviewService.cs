using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AiReviewer.Shared
{
    public class ReviewResult
    {
        public string FilePath { get; set; } = "";
        public int LineNumber { get; set; }
        public string Severity { get; set; } = "Medium";
        public string Issue { get; set; } = "";
        public string Suggestion { get; set; } = "";
        public string Rule { get; set; } = "";
        public string CodeSnippet { get; set; } = "";
        public string FixedCode { get; set; } = "";
        public string RepositoryPath { get; set; } = "";
    }

    public class AiReviewService
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName;

        public AiReviewService(string endpoint, string apiKey, string deploymentName)
        {
            _endpoint = endpoint;
            _apiKey = apiKey;
            _deploymentName = deploymentName;
        }

        public async Task<List<ReviewResult>> ReviewCodeAsync(List<Patch> patches, MerlinConfig config)
        {
            var client = new AzureOpenAIClient(new Uri(_endpoint), new AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_deploymentName);
            
            // Build the prompt
            var prompt = BuildReviewPrompt(patches, config);
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert code reviewer. Analyze code changes and provide specific, actionable feedback. Focus on code quality, readability, potential bugs, security issues, and best practices."),
                new UserChatMessage(prompt)
            };

            var completion = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            });

            var reviewText = completion.Value.Content[0].Text;

            // Parse the AI response into structured results
            var results = ParseReviewResponse(reviewText, patches);
            
            // Add code snippets to results
            foreach (var result in results)
            {
                result.CodeSnippet = ExtractCodeSnippet(patches, result.FilePath, result.LineNumber);
            }
            
            return results;
        }
        
        private string ExtractCodeSnippet(List<Patch> patches, string filePath, int lineNumber)
        {
            // Normalize paths for comparison (forward slashes, case insensitive)
            var normalizedTarget = filePath.Replace("\\", "/").ToLowerInvariant();
            
            // Try exact match first
            var patch = patches.Find(p => p.FilePath.Replace("\\", "/").Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase));
            
            // If not found, try EndsWith match (for partial paths from AI)
            if (patch == null)
            {
                patch = patches.Find(p => 
                {
                    var patchPath = p.FilePath.Replace("\\", "/").ToLowerInvariant();
                    return patchPath.EndsWith(normalizedTarget) || normalizedTarget.EndsWith(patchPath);
                });
            }
            
            if (patch == null)
                return "";
                
            foreach (var hunk in patch.Hunks)
            {
                int currentLine = hunk.StartLine;
                foreach (var line in hunk.Lines)
                {
                    // Lines now have prefixes: " " for context, "+" for additions
                    if (currentLine == lineNumber)
                    {
                        // Return without prefix
                        if (line.StartsWith("+") || line.StartsWith(" "))
                            return line.Substring(1).TrimStart();
                        return line.TrimStart();
                    }
                    
                    // Only increment for context and additions
                    if (line.StartsWith(" ") || line.StartsWith("+"))
                        currentLine++;
                }
            }
            
            return "";
        }

        private string BuildReviewPrompt(List<Patch> patches, MerlinConfig config)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("You are a SENIOR SOFTWARE ENGINEER doing a THOROUGH code review. Be CRITICAL and DETAIL-ORIENTED.");
            sb.AppendLine("Examine EVERY line of changed code carefully. Don't just catch obvious issues - look deeper!");
            sb.AppendLine();
            sb.AppendLine("=== COMPREHENSIVE REVIEW CHECKLIST ===");
            sb.AppendLine();
            sb.AppendLine("🔒 SECURITY (High Priority):");
            sb.AppendLine("- SQL injection, XSS, command injection");
            sb.AppendLine("- Hardcoded secrets, passwords, API keys, connection strings");
            sb.AppendLine("- Insecure cryptography or random number generation");
            sb.AppendLine("- Missing authentication/authorization checks");
            sb.AppendLine("- Path traversal, directory traversal vulnerabilities");
            sb.AppendLine("- Insecure deserialization");
            sb.AppendLine();
            sb.AppendLine("⚡ PERFORMANCE:");
            sb.AppendLine("- N+1 database queries, missing batch operations");
            sb.AppendLine("- Blocking calls in async code, missing async/await");
            sb.AppendLine("- Inefficient LINQ queries, multiple enumerations");
            sb.AppendLine("- String concatenation in loops (use StringBuilder)");
            sb.AppendLine("- Missing caching, repeated expensive operations");
            sb.AppendLine("- Large object allocations, unnecessary boxing");
            sb.AppendLine();
            sb.AppendLine("🛡️ RELIABILITY & ERROR HANDLING:");
            sb.AppendLine("- Missing null checks (check parameters, return values)");
            sb.AppendLine("- Unhandled exceptions, empty catch blocks");
            sb.AppendLine("- Resource leaks (missing using/Dispose for IDisposable)");
            sb.AppendLine("- Race conditions, non-thread-safe operations");
            sb.AppendLine("- Division by zero, array index out of bounds");
            sb.AppendLine("- Missing validation of external inputs");
            sb.AppendLine();
            sb.AppendLine("🎯 CODE QUALITY & MAINTAINABILITY:");
            sb.AppendLine("- Console.WriteLine/Debug.Print in production code → Use proper logging");
            sb.AppendLine("- Poor logging: vague messages, typos, debug text like 'hERRE', 'test', 'TODO'");
            sb.AppendLine("- Meaningful messages: Log WHAT happened, WHY, with CONTEXT (IDs, states, values)");
            sb.AppendLine("- Magic numbers without explanation");
            sb.AppendLine("- Unclear variable/method names (single letters, abbreviations, vague names)");
            sb.AppendLine("- Code duplication (repeated logic)");
            sb.AppendLine("- Methods doing too much (Single Responsibility Principle)");
            sb.AppendLine("- Deep nesting, complex conditionals");
            sb.AppendLine("- Commented-out code (remove it)");
            sb.AppendLine("- Dead code, unused variables");
            sb.AppendLine();
            sb.AppendLine("📝 BEST PRACTICES:");
            sb.AppendLine("- SOLID principles violations");
            sb.AppendLine("- Missing ConfigureAwait(false) in library code");
            sb.AppendLine("- Fire-and-forget async (await Task without proper handling)");
            sb.AppendLine("- Inconsistent naming conventions (PascalCase, camelCase)");
            sb.AppendLine("- Missing XML documentation on public APIs");
            sb.AppendLine("- Hardcoded configuration values (use config files)");
            sb.AppendLine();
            sb.AppendLine("✍️ DOCUMENTATION & GRAMMAR:");
            sb.AppendLine("- Spelling errors in comments (e.g., 'recieve' → 'receive', 'occured' → 'occurred')");
            sb.AppendLine("- Grammar mistakes in comments (incomplete sentences, wrong tense, unclear phrasing)");
            sb.AppendLine("- Typos in string literals and user-facing messages");
            sb.AppendLine("- Missing comments on complex logic, algorithms, or non-obvious code");
            sb.AppendLine("- **CRITICAL**: Comments that contradict the actual code logic (e.g., comment says '!=' but code uses '==')");
            sb.AppendLine("- Comments describing the WRONG condition, branch, or behavior");
            sb.AppendLine("- Outdated comments from previous implementations that no longer apply");
            sb.AppendLine("- Poorly written comments (unclear, vague, or uninformative)");
            sb.AppendLine("- Missing TODO/FIXME/HACK markers where appropriate");
            sb.AppendLine("- Inconsistent comment style (mix of // and /* */, inconsistent capitalization)");
            sb.AppendLine();
            sb.AppendLine("🔍 SPECIFIC THINGS TO CRITIQUE:");
            sb.AppendLine("1. Log messages: Are they meaningful? Do they have typos? Do they provide context?");
            sb.AppendLine("2. Method complexity: Is the method doing one thing or multiple responsibilities?");
            sb.AppendLine("3. Variable names: Are they clear and self-documenting?");
            sb.AppendLine("4. Business logic: Does the code make sense? Any logical errors?");
            sb.AppendLine("5. Edge cases: What happens with null, empty, negative, or boundary values?");
            sb.AppendLine("6. Comments & Documentation:");
            sb.AppendLine("   - Check spelling, grammar, clarity");
            sb.AppendLine("   - **VERIFY comments match the code** (if comment says 'when X != Y' but code checks 'X == Y', flag it!)");
            sb.AppendLine("   - Read comment, read code, confirm they describe the SAME logic");
            sb.AppendLine("   - Suggest comments for complex code");
            sb.AppendLine("7. String literals: Check for typos in error messages, user-facing text, log messages.");
            sb.AppendLine();
            sb.AppendLine("=== SEVERITY GUIDELINES ===");
            sb.AppendLine("High: Security holes, crashes, data corruption, production issues");
            sb.AppendLine("Medium: Performance degradation, poor error handling, code smells, maintainability issues");
            sb.AppendLine("Low: Style issues, minor improvements, suggestions for better readability");
            sb.AppendLine();
            sb.AppendLine("=== OUTPUT FORMAT (MANDATORY) ===");
            sb.AppendLine("For EACH issue, you MUST provide ALL fields:");
            sb.AppendLine("FILE: <file path>");
            sb.AppendLine("LINE: <line number>");
            sb.AppendLine("SEVERITY: High|Medium|Low");
            sb.AppendLine("ISSUE: <detailed explanation of the problem>");
            sb.AppendLine("SUGGESTION: <specific, actionable improvement with reasoning>");
            sb.AppendLine("FIXEDCODE: <THE EXACT CORRECTED LINE - MANDATORY, NO EXCEPTIONS>");
            sb.AppendLine("RULE: <category: Security|Performance|Reliability|Code Quality|Best Practices|Documentation>");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("🔴 CRITICAL REQUIREMENT:");
            sb.AppendLine("EVERY issue MUST have a FIXEDCODE field. No exceptions!");
            sb.AppendLine("- Spelling error in comment? → FIXEDCODE: // we will be returning in case if the ext.version == clusterExtensionVersion");
            sb.AppendLine("- Console.WriteLine? → FIXEDCODE: _logger.Debug(\"message\");");
            sb.AppendLine("- Wrong operator? → FIXEDCODE: if (x == y)");
            sb.AppendLine("If you don't provide FIXEDCODE, the developer cannot apply the fix!");
            sb.AppendLine();
            sb.AppendLine("🚨 WHAT TO REVIEW:");
            sb.AppendLine();
            sb.AppendLine("PRIMARY: Lines marked '← NEW LINE' (changed/added code)");
            sb.AppendLine("- Review ALL new lines for security, performance, reliability, code quality, etc.");
            sb.AppendLine();
            sb.AppendLine("SECONDARY: Context lines DIRECTLY AFFECTED by the changes");
            sb.AppendLine("- If a new comment contradicts existing code in context → FLAG IT (report on the NEW comment line)");
            sb.AppendLine("- If a new variable name conflicts with existing ones in context → FLAG IT");
            sb.AppendLine("- If new code breaks existing logic patterns in context → FLAG IT");
            sb.AppendLine("- If new code makes existing context code unreachable/redundant → FLAG IT");
            sb.AppendLine();
            sb.AppendLine("DO NOT REPORT: Issues in context lines unrelated to the changes");
            sb.AppendLine("- Old code with Console.WriteLine that wasn't touched → IGNORE");
            sb.AppendLine("- Old spelling errors in unchanged comments → IGNORE");
            sb.AppendLine("- Existing performance issues not related to changes → IGNORE");
            sb.AppendLine();
            sb.AppendLine("FOCUS: What did the developer CHANGE and how does it impact surrounding code?");
            sb.AppendLine();
            sb.AppendLine("Review Guidelines:");
            sb.AppendLine("- Review EVERY line marked '← NEW LINE' thoroughly");
            sb.AppendLine("- Check if changes introduce inconsistencies with context");
            sb.AppendLine("- Verify new comments describe actual logic (not opposite)");
            sb.AppendLine("- Report MULTIPLE issues per line if they exist");
            sb.AppendLine("- Provide context-aware fixes that match the surrounding code");
            sb.AppendLine();
            sb.AppendLine("🔍 COMMENT-CODE VERIFICATION (CRITICAL):");
            sb.AppendLine("- When you see a comment above/near an if-statement, VERIFY the comment matches the actual condition");
            sb.AppendLine("- Check operators carefully: Does comment say '!=' but code uses '=='? Does comment say '<' but code uses '>'?");
            sb.AppendLine("- Check logic flow: Does comment say 'when true' but code checks 'when false'? ");
            sb.AppendLine("- Understand the ACTUAL logic by reading surrounding code, then check if comment describes it correctly");
            sb.AppendLine("- If comment contradicts code logic, flag it as Documentation issue and provide corrected comment in FIXEDCODE");
            sb.AppendLine();

            // Add MerlinBot rules
            if (config?.Checks != null && config.Checks.Count > 0)
            {
                sb.AppendLine("Project-Specific Rules to Check:");
                foreach (var check in config.Checks)
                {
                    sb.AppendLine($"- {check.Id}: {check.Description}");
                    if (!string.IsNullOrEmpty(check.Guidance))
                    {
                        sb.AppendLine($"  Guidance: {check.Guidance}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("Code Changes:");
            sb.AppendLine();

            // Add each file's diff
            foreach (var patch in patches)
            {
                sb.AppendLine($"=== File: {patch.FilePath} ===");
                
                foreach (var hunk in patch.Hunks)
                {
                    sb.AppendLine($"Starting at line {hunk.StartLine}:");
                    int lineNum = hunk.StartLine;
                    foreach (var line in hunk.Lines)
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

        private List<ReviewResult> ParseReviewResponse(string response, List<Patch> patches)
        {
            var results = new List<ReviewResult>();
            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            ReviewResult? current = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Handle both "FILE:" and "**FILE:**" formats
                if (trimmed.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("**FILE:**", StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null)
                    {
                        results.Add(current);
                    }
                    
                    // Extract file path - handle markdown formatting
                    var filePath = trimmed;
                    if (filePath.StartsWith("**FILE:**"))
                        filePath = filePath.Substring(9);
                    else
                        filePath = filePath.Substring(5);
                    
                    // Remove markdown backticks and asterisks
                    filePath = filePath.Trim('*', '`', ' ');
                    
                    current = new ReviewResult
                    {
                        FilePath = filePath
                    };
                }
                else if (current != null)
                {
                    // Handle both "LINE:" and "**LINE:**" formats
                    if (trimmed.StartsWith("LINE:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("**LINE:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var lineText = trimmed;
                        if (lineText.StartsWith("**LINE:**"))
                            lineText = lineText.Substring(9);
                        else
                            lineText = lineText.Substring(5);
                        
                        lineText = lineText.Trim('*', ' ');
                        
                        if (int.TryParse(lineText, out int lineNum))
                        {
                            current.LineNumber = lineNum;
                        }
                    }
                    else if (trimmed.StartsWith("SEVERITY:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**SEVERITY:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var severity = trimmed;
                        if (severity.StartsWith("**SEVERITY:**"))
                            severity = severity.Substring(13);
                        else
                            severity = severity.Substring(9);
                        
                        current.Severity = severity.Trim('*', ' ');
                    }
                    else if (trimmed.StartsWith("ISSUE:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**ISSUE:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var issue = trimmed;
                        if (issue.StartsWith("**ISSUE:**"))
                            issue = issue.Substring(10);
                        else
                            issue = issue.Substring(6);
                        current.Issue = issue.Trim('*', ' ');
                    }
                    else if (trimmed.StartsWith("SUGGESTION:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**SUGGESTION:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var suggestion = trimmed;
                        if (suggestion.StartsWith("**SUGGESTION:**"))
                            suggestion = suggestion.Substring(15);
                        else
                            suggestion = suggestion.Substring(11);
                        current.Suggestion = suggestion.Trim('*', ' ');
                    }
                    else if (trimmed.StartsWith("FIXEDCODE:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**FIXEDCODE:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var fixedCode = trimmed;
                        if (fixedCode.StartsWith("**FIXEDCODE:**"))
                            fixedCode = fixedCode.Substring(14);
                        else
                            fixedCode = fixedCode.Substring(10);
                        current.FixedCode = fixedCode.Trim('*', '`', ' ');
                    }
                    else if (trimmed.StartsWith("RULE:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**RULE:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var rule = trimmed;
                        if (rule.StartsWith("**RULE:**"))
                            rule = rule.Substring(9);
                        else
                            rule = rule.Substring(5);
                        current.Rule = rule.Trim('*', ' ');
                    }
                    else if (trimmed == "---")
                    {
                        if (current != null && !string.IsNullOrEmpty(current.Issue))
                        {
                            results.Add(current);
                            current = null;
                        }
                    }
                }
            }

            // Add last item if exists
            if (current != null && !string.IsNullOrEmpty(current.Issue))
            {
                results.Add(current);
            }

            return results;
        }
    }
}
