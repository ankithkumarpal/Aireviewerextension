using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using AiReviewer.Shared.Services;
using AiReviewer.Shared.Enum;
using AiReviewer.Shared.Models;

namespace AiReviewer.Shared
{
    public class AiReviewService
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName;
        private string _currentRepositoryPath;
        private TeamLearningApiClient _teamApiClient;

        public AiReviewService(string endpoint, string apiKey, string deploymentName)
        {
            _endpoint = endpoint;
            _apiKey = apiKey;
            _deploymentName = deploymentName;
        }

        /// <summary>
        /// Sets the Team Learning API client for pattern retrieval
        /// </summary>
        public void SetTeamApiClient(TeamLearningApiClient client)
        {
            _teamApiClient = client;
        }

        /// <summary>
        /// Reviews code with streaming progress updates for better UX
        /// </summary>
        public async Task<List<ReviewResult>> ReviewCodeWithStreamingAsync(
            List<Patch> patches, 
            StagebotConfig config, 
            string repositoryPath,
            Action<ReviewProgressUpdate> onProgress)
        {
            _currentRepositoryPath = repositoryPath;
            
            // Report: Started
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.Started,
                Message = $"Starting review of {patches.Count} file(s)...",
                TotalFiles = patches.Count
            });

            var client = new AzureOpenAIClient(new Uri(_endpoint), new ApiKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_deploymentName);

            // Report: Building Prompt
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.BuildingPrompt,
                Message = "Analyzing code and building context..."
            });

            var prompt = BuildReviewPrompt(patches, config);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert code reviewer. Analyze code changes and provide specific, actionable feedback. Focus on code quality, readability, potential bugs, security issues, and best practices."),
                new UserChatMessage(prompt)
            };

            // Report: Calling AI
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.CallingAI,
                Message = "Sending to AI for review..."
            });

            // Use streaming API
            var responseBuilder = new StringBuilder();
            var streamingOptions = new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            };

            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, streamingOptions))
            {
                foreach (var contentPart in update.ContentUpdate)
                {
                    responseBuilder.Append(contentPart.Text);
                    
                    // Report streaming progress every ~500 chars
                    if (responseBuilder.Length % 500 < 50)
                    {
                        onProgress?.Invoke(new ReviewProgressUpdate
                        {
                            Type = ReviewProgressType.Streaming,
                            Message = $"Receiving AI response... ({responseBuilder.Length} chars)",
                            PartialResponse = responseBuilder.ToString(),
                            ProcessedTokens = responseBuilder.Length / 4 // Rough token estimate
                        });
                    }
                }
            }

            var reviewText = responseBuilder.ToString();

            // Report: Parsing
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.ParsingResults,
                Message = "Parsing review results..."
            });

            var results = ParseReviewResponse(reviewText, patches);

            // Add code snippets
            foreach (var result in results)
            {
                result.CodeSnippet = ExtractCodeSnippet(patches, result.FilePath, result.LineNumber);
            }

            // Report: Completed
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.Completed,
                Message = $"Review complete! Found {results.Count} issue(s).",
                PartialResults = results
            });

            System.Diagnostics.Debug.WriteLine($"Streaming review complete: {results.Count} issues");
            return results;
        }

        /// <summary>
        /// Reviews code (non-streaming version for backward compatibility)
        /// </summary>
        public async Task<List<ReviewResult>> ReviewCodeAsync(List<Patch> patches, StagebotConfig config, string repositoryPath = null)
        {
            // Store repository path for learning system
            _currentRepositoryPath = repositoryPath;
            
            // DEBUG: Log patches being reviewed
            System.Diagnostics.Debug.WriteLine($"=== AI REVIEW START ===");
            System.Diagnostics.Debug.WriteLine($"Reviewing {patches.Count} file(s)");
            foreach (var p in patches)
            {
                System.Diagnostics.Debug.WriteLine($"  - {p.FilePath}: {p.Hunks.Count} hunk(s)");
            }
            
            var client = new AzureOpenAIClient(new Uri(_endpoint), new ApiKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_deploymentName);
            
            // Build the prompt
            var prompt = BuildReviewPrompt(patches, config);
            
            // DEBUG: Log prompt length
            System.Diagnostics.Debug.WriteLine($"Prompt length: {prompt.Length} chars");
            System.Diagnostics.Debug.WriteLine($"Prompt preview (first 1000 chars):\n{prompt.Substring(0, Math.Min(1000, prompt.Length))}");
            System.Diagnostics.Debug.WriteLine($"---");
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert code reviewer. Analyze code changes and provide specific, actionable feedback. Focus on code quality, readability, potential bugs, security issues, and best practices."),
                new UserChatMessage(prompt)
            };

            System.Diagnostics.Debug.WriteLine($"Calling Azure OpenAI API...");
            var completion = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            });

            var reviewText = completion.Value.Content[0].Text;
            System.Diagnostics.Debug.WriteLine($"API call completed successfully!");

            // DEBUG: Log AI response
            System.Diagnostics.Debug.WriteLine($"AI Response Length: {reviewText?.Length ?? 0}");
            if (reviewText != null && reviewText.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"=== FULL AI RESPONSE ===");
                System.Diagnostics.Debug.WriteLine(reviewText);
                System.Diagnostics.Debug.WriteLine($"=== END AI RESPONSE ===");
            }

            // Parse the AI response into structured results
            var results = ParseReviewResponse(reviewText, patches);
            
            // DEBUG: Log parsing result
            System.Diagnostics.Debug.WriteLine($"PASS 1: Parsed {results.Count} issues from AI response");
            if (results.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: No issues parsed! Check if AI response format matches expected format.");
            }
            else
            {
                foreach (var r in results)
                {
                    System.Diagnostics.Debug.WriteLine($"  Issue: {r.FilePath}:{r.LineNumber} - {r.Severity} - {r.Issue}");
                }
            }
            
            // Add code snippets to results
            foreach (var result in results)
            {
                result.CodeSnippet = ExtractCodeSnippet(patches, result.FilePath, result.LineNumber);
            }
            
            // PASS 2: Validate the fixes (DISABLED - was filtering too aggressively)
            // if (results.Count > 0)
            // {
            //     System.Diagnostics.Debug.WriteLine($"=== STARTING PASS 2: VALIDATION ===");
            //     var validatedResults = await ValidateFixesAsync(chatClient, results, patches);
            //     System.Diagnostics.Debug.WriteLine($"PASS 2: {validatedResults.Count} issues validated (filtered {results.Count - validatedResults.Count} false positives)");
            //     System.Diagnostics.Debug.WriteLine($"=== AI REVIEW END ===");
            //     return validatedResults;
            // }
            
            System.Diagnostics.Debug.WriteLine($"=== AI REVIEW END ===");
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

        private string BuildReviewPrompt(List<Patch> patches, StagebotConfig config)
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
            sb.AppendLine("� OOP PRINCIPLES (FUNDAMENTAL):");
            sb.AppendLine("- **Encapsulation Violations**:");
            sb.AppendLine("  • Public fields instead of properties (breaks encapsulation)");
            sb.AppendLine("  • Missing validation in setters (allowing invalid state)");
            sb.AppendLine("  • Exposing internal collections directly (return IReadOnlyList/IEnumerable instead)");
            sb.AppendLine("  • Breaking information hiding (exposing implementation details)");
            sb.AppendLine("- **Inheritance Issues**:");
            sb.AppendLine("  • Deep inheritance hierarchies (>3 levels, prefer composition)");
            sb.AppendLine("  • Violating Liskov Substitution Principle (subclass breaks parent contract)");
            sb.AppendLine("  • Using inheritance for code reuse instead of composition");
            sb.AppendLine("  • Missing virtual/override keywords where needed");
            sb.AppendLine("  • Sealed classes that should be inheritable (or vice versa)");
            sb.AppendLine("- **Polymorphism Misuse**:");
            sb.AppendLine("  • Type checking instead of polymorphism (if (obj is Type) → use virtual methods)");
            sb.AppendLine("  • Casting instead of using generic types");
            sb.AppendLine("  • Missing interfaces for abstraction");
            sb.AppendLine("  • Not using polymorphism where it simplifies code (Strategy pattern)");
            sb.AppendLine();
            sb.AppendLine("🔷 C# & .NET SPECIFIC PATTERNS:");
            sb.AppendLine("- **Async/Await Best Practices**:");
            sb.AppendLine("  • Async void methods (should be async Task, except event handlers)");
            sb.AppendLine("  • Missing CancellationToken support in async methods");
            sb.AppendLine("  • Blocking on async code (.Result, .Wait() → use await)");
            sb.AppendLine("  • Not using ValueTask for hot paths with common synchronous completion");
            sb.AppendLine("  • Missing ConfigureAwait(false) in library code (causes deadlocks)");
            sb.AppendLine("  • Async over sync (wrapping synchronous code in Task.Run unnecessarily)");
            sb.AppendLine("- **LINQ Optimization**:");
            sb.AppendLine("  • Multiple enumeration (.ToList() missing when enumerating multiple times)");
            sb.AppendLine("  • Using .Where().Count() instead of .Count(predicate)");
            sb.AppendLine("  • Using .Where().Any() instead of .Any(predicate)");
            sb.AppendLine("  • Using .Where().First() instead of .First(predicate)");
            sb.AppendLine("  • Inefficient queries (use AsParallel() for CPU-bound operations)");
            sb.AppendLine("  • Materializing entire sequences unnecessarily");
            sb.AppendLine("- **IDisposable & Resource Management**:");
            sb.AppendLine("  • Missing using statements for IDisposable objects");
            sb.AppendLine("  • Not implementing IDisposable when class owns unmanaged resources");
            sb.AppendLine("  • Missing Dispose(bool disposing) pattern for inheritance");
            sb.AppendLine("  • Finalizers without IDisposable implementation");
            sb.AppendLine("  • Using GC.SuppressFinalize without implementing finalizer");
            sb.AppendLine("- **Null Handling (C# 8+)**:");
            sb.AppendLine("  • Not using nullable reference types (#nullable enable)");
            sb.AppendLine("  • Missing null-coalescing operators (??, ??=)");
            sb.AppendLine("  • Not using null-conditional operators (?., ?[])");
            sb.AppendLine("  • Using 'if (x != null)' instead of pattern matching 'if (x is { })'");
            sb.AppendLine("- **Modern C# Features**:");
            sb.AppendLine("  • Not using pattern matching where appropriate (switch expressions, is patterns)");
            sb.AppendLine("  • Verbose property declarations (use expression-bodied members, init accessors)");
            sb.AppendLine("  • Not using records for immutable data");
            sb.AppendLine("  • Using String.Format instead of string interpolation ($\"\")");
            sb.AppendLine("  • Not using collection expressions (C# 12+): [] instead of new List<T>()");
            sb.AppendLine("  • Not using file-scoped namespaces");
            sb.AppendLine("- **Exception Handling**:");
            sb.AppendLine("  • Catching System.Exception (too broad, catch specific exceptions)");
            sb.AppendLine("  • Empty catch blocks (at least log the exception)");
            sb.AppendLine("  • Using exceptions for control flow (expensive, use Try* pattern)");
            sb.AppendLine("  • Throwing Exception instead of specific exception types");
            sb.AppendLine("  • Not preserving stack trace (throw ex instead of throw)");
            sb.AppendLine();
            sb.AppendLine("�🏗️ DESIGN PATTERNS & ANTI-PATTERNS:");
            sb.AppendLine("- **God Class**: Class with too many responsibilities (>500 lines, >10 methods, doing unrelated things)");
            sb.AppendLine("- **Singleton Abuse**: Using Singleton when not needed, making testing difficult");
            sb.AppendLine("- **Tight Coupling**: Classes directly instantiating dependencies instead of injection");
            sb.AppendLine("- **Feature Envy**: Method using more data from another class than its own");
            sb.AppendLine("- **Long Parameter List**: Methods with >4 parameters (use parameter object pattern)");
            sb.AppendLine("- **Primitive Obsession**: Using primitives instead of small objects (e.g., string for email/phone)");
            sb.AppendLine("- **Switch Statement Smell**: Large switch/if-else chains that should use polymorphism/strategy pattern");
            sb.AppendLine("- **Circular Dependencies**: Classes depending on each other (A → B → A)");
            sb.AppendLine("- **Data Clumps**: Same group of data parameters appearing together (create a class)");
            sb.AppendLine("- **Missing Patterns**: Suggest Factory, Strategy, Repository, or other patterns when appropriate");
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
            sb.AppendLine("CONFIDENCE: High|Medium|Low");
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
            sb.AppendLine("=== CONFIDENCE LEVEL GUIDELINES ===");
            sb.AppendLine("High Confidence:");
            sb.AppendLine("- Objective issues: syntax errors, security vulnerabilities (hardcoded secrets, SQL injection)");
            sb.AppendLine("- Clear violations: Console.WriteLine in production, missing null checks with obvious NPE risk");
            sb.AppendLine("- Spelling/grammar errors in comments or strings");
            sb.AppendLine("- Definite bugs: off-by-one, wrong operator, unreachable code");
            sb.AppendLine();
            sb.AppendLine("Medium Confidence:");
            sb.AppendLine("- Code smells: long methods, god classes, tight coupling");
            sb.AppendLine("- Performance concerns: N+1 queries, inefficient algorithms");
            sb.AppendLine("- Best practice violations: missing async/await, fire-and-forget");
            sb.AppendLine("- Design pattern suggestions (Factory, Strategy, etc.)");
            sb.AppendLine();
            sb.AppendLine("Low Confidence:");
            sb.AppendLine("- Subjective style preferences: naming conventions, code organization");
            sb.AppendLine("- Speculative improvements without clear business context");
            sb.AppendLine("- Suggestions that might not apply to specific use case");
            sb.AppendLine("- Architectural changes that need more information");
            sb.AppendLine();
            sb.AppendLine("🚨 CRITICAL: WHAT TO REVIEW (READ THIS CAREFULLY!)");
            sb.AppendLine();
            sb.AppendLine("⚠️ CONTEXT IS FOR UNDERSTANDING ONLY - NOT FOR REVIEWING!");
            sb.AppendLine("We provide 'FULL FILE CONTEXT' or 'PARTIAL FILE CONTEXT' so you can understand:");
            sb.AppendLine("- The class structure, fields, and dependencies");
            sb.AppendLine("- How the changed code fits into the bigger picture");
            sb.AppendLine("- Naming conventions and patterns used in the file");
            sb.AppendLine("DO NOT report issues on context lines unless the CHANGE directly breaks them!");
            sb.AppendLine();
            sb.AppendLine("✅ ONLY REVIEW: Lines marked with '← NEW LINE' or '←CHANGED'");
            sb.AppendLine("- These are the ONLY lines the developer modified");
            sb.AppendLine("- Report issues ONLY on these marked lines");
            sb.AppendLine("- Use context to UNDERSTAND, but don't review context itself");
            sb.AppendLine();
            sb.AppendLine("❌ DO NOT REVIEW OR REPORT:");
            sb.AppendLine("- Lines WITHOUT '← NEW LINE' or '←CHANGED' markers");
            sb.AppendLine("- Old code in the header/context sections");
            sb.AppendLine("- Existing issues in unchanged code (even if you see Console.WriteLine, spelling errors, etc.)");
            sb.AppendLine("- Pre-existing code smells that weren't introduced by this change");
            sb.AppendLine();
            sb.AppendLine("🎯 YOUR FOCUS:");
            sb.AppendLine("1. Find issues in the CHANGED lines only");
            sb.AppendLine("2. Use context to understand if the change is correct");
            sb.AppendLine("3. Report if a change BREAKS or CONFLICTS with existing code");
            sb.AppendLine("4. But NEVER report issues on unchanged lines themselves");
            sb.AppendLine();
            sb.AppendLine("Example of CORRECT behavior:");
            sb.AppendLine("- Changed line has Console.WriteLine → REPORT IT ✅");
            sb.AppendLine("- Context line has Console.WriteLine → IGNORE IT ❌");
            sb.AppendLine("- Changed line duplicates logic from context → REPORT on changed line ✅");
            sb.AppendLine("- Context has old bug unrelated to change → IGNORE IT ❌");
            sb.AppendLine();
            sb.AppendLine("Review Guidelines:");
            sb.AppendLine("- Review EVERY line marked '← NEW LINE' thoroughly with ALL checklist categories");
            sb.AppendLine("- Check if changes introduce inconsistencies with context");
            sb.AppendLine("- Verify new comments describe actual logic (not opposite)");
            sb.AppendLine("- Report MULTIPLE issues per line if they exist (don't stop at first issue)");
            sb.AppendLine("- Provide context-aware fixes that match the surrounding code style");
            sb.AppendLine("- Look for design patterns and anti-patterns in the new/changed code");
            sb.AppendLine("- Suggest modern C# features when applicable (C# 8-12)");
            sb.AppendLine("- If you see a class growing too large (context shows many methods), flag it as God Class");
            sb.AppendLine("- If you see a method with many parameters (>4), suggest parameter object pattern");
            sb.AppendLine("- Check for proper use of interfaces, abstract classes, and inheritance hierarchies");
            sb.AppendLine("- Validate proper encapsulation (no public fields, proper property usage)");
            sb.AppendLine();
            sb.AppendLine("🔍 COMMENT-CODE VERIFICATION (CRITICAL):");
            sb.AppendLine("- When you see a comment above/near an if-statement, VERIFY the comment matches the actual condition");
            sb.AppendLine("- Check operators carefully: Does comment say '!=' but code uses '=='? Does comment say '<' but code uses '>'?");
            sb.AppendLine("- Check logic flow: Does comment say 'when true' but code checks 'when false'? ");
            sb.AppendLine("- Understand the ACTUAL logic by reading surrounding code, then check if comment describes it correctly");
            sb.AppendLine("- If comment contradicts code logic, flag it as Documentation issue and provide corrected comment in FIXEDCODE");
            sb.AppendLine();

            // Add Stagebot rules
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

            // AI LEARNING: Inject patterns learned from user feedback stored.
            try
            {
                if (!string.IsNullOrEmpty(_currentRepositoryPath) && _teamApiClient != null)
                {
                    var patternAnalyzer = new PatternAnalyzer(_currentRepositoryPath, _teamApiClient);
                    
                    // Get relevant few-shot examples from Azure
                    var fileExtension = patches.FirstOrDefault()?.FilePath != null 
                        ? Path.GetExtension(patches.First().FilePath)?.ToLowerInvariant() 
                        : ".cs";
                    
                    var examples = patternAnalyzer.GetRelevantPatterns(fileExtension ?? ".cs", maxPatterns: 15, minAccuracy: 35.0);
                    
                    if (examples.Count > 0)
                    {
                        var learningSection = patternAnalyzer.FormatExamplesForPrompt(examples);
                        sb.Append(learningSection);
                        System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Injected {examples.Count} learned patterns into prompt");
                    }
                }
            }
            catch (Exception ex) { 
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Warning: Could not load learned patterns: {ex.Message}");
            }

            sb.AppendLine("Code Changes:");
            sb.AppendLine();

            // Add each file's diff WITH full file context
            foreach (var patch in patches)
            {
                sb.AppendLine($"=== File: {patch.FilePath} ===");
                
                // Try to include full file context for better AI understanding
                if (!string.IsNullOrEmpty(_currentRepositoryPath))
                {
                    var fullFilePath = Path.Combine(_currentRepositoryPath, patch.FilePath.Replace("/", "\\"));
                    if (File.Exists(fullFilePath))
                    {
                        try
                        {
                            var fileContent = File.ReadAllText(fullFilePath);
                            var lines = fileContent.Split('\n');
                            
                            // Get the changed line numbers from hunks
                            var changedLines = patch.Hunks.SelectMany(h => 
                            {
                                var result = new List<int>();
                                int lineNum = h.StartLine;
                                foreach (var l in h.Lines)
                                {
                                    if (l.StartsWith("+")) result.Add(lineNum);
                                    if (l.StartsWith(" ") || l.StartsWith("+")) lineNum++;
                                }
                                return result;
                            }).ToList();
                            
                            if (lines.Length <= 1000 && fileContent.Length <= 40000)
                            {
                                // Small file: include everything
                                sb.AppendLine("FULL FILE CONTEXT (for better understanding):");
                                sb.AppendLine("```");
                                int lineNum = 1;
                                foreach (var line in lines)
                                {
                                    sb.AppendLine($"{lineNum,4}: {line.TrimEnd('\r')}");
                                    lineNum++;
                                }
                                sb.AppendLine("```");
                                sb.AppendLine();
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
                                    sb.AppendLine($"{i + 1,4}: {lines[i].TrimEnd('\r')}");
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
                                            contextWindows.Add((windowStart, windowEnd, windowChanges));
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
                            }
                        }
                        catch { /* Ignore file read errors */ }
                    }
                }
                
                sb.AppendLine("CHANGED LINES (review these specifically):");
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
                    else if (trimmed.StartsWith("CONFIDENCE:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**CONFIDENCE:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var confidence = trimmed;
                        if (confidence.StartsWith("**CONFIDENCE:**"))
                            confidence = confidence.Substring(15);
                        else
                            confidence = confidence.Substring(11);
                        
                        current.Confidence = confidence.Trim('*', ' ');
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

        private async Task<List<ReviewResult>> ValidateFixesAsync(ChatClient chatClient, List<ReviewResult> results, List<Patch> patches)
        {
            var validatedResults = new List<ReviewResult>();
            
            // Build validation prompt
            var sb = new StringBuilder();
            sb.AppendLine("You are validating code review suggestions. Your job is to:");
            sb.AppendLine("1. Verify the issue is real (not a false positive)");
            sb.AppendLine("2. Check if the FIXEDCODE actually solves the problem");
            sb.AppendLine("3. Ensure the fix doesn't introduce new issues");
            sb.AppendLine();
            sb.AppendLine("For each issue below, respond with ONLY:");
            sb.AppendLine("VALID: YES or NO");
            sb.AppendLine("REASON: Brief explanation");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("Review these issues:");
            sb.AppendLine();
            
            int issueNum = 1;
            foreach (var result in results)
            {
                // Only validate Medium and High confidence issues
                if (result.Confidence != "Low")
                {
                    sb.AppendLine($"ISSUE #{issueNum}:");
                    sb.AppendLine($"File: {result.FilePath}");
                    sb.AppendLine($"Line: {result.LineNumber}");
                    sb.AppendLine($"Severity: {result.Severity}");
                    sb.AppendLine($"Confidence: {result.Confidence}");
                    sb.AppendLine($"Problem: {result.Issue}");
                    sb.AppendLine($"Original Code: {result.CodeSnippet}");
                    sb.AppendLine($"Suggested Fix: {result.FixedCode}");
                    sb.AppendLine($"Suggestion: {result.Suggestion}");
                    sb.AppendLine();
                    issueNum++;
                }
            }
            
            var validationMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a validation expert. Be critical and strict - reject false positives and ineffective fixes."),
                new UserChatMessage(sb.ToString())
            };

            System.Diagnostics.Debug.WriteLine($"Validation prompt length: {sb.Length} chars");
            System.Diagnostics.Debug.WriteLine($"Calling validation API...");
            
            var validationCompletion = await chatClient.CompleteChatAsync(validationMessages, new ChatCompletionOptions
            {
                Temperature = 0.1f,  // Lower temperature for more consistent validation
                MaxOutputTokenCount = 1500
            });

            var validationText = validationCompletion.Value.Content[0].Text;
            System.Diagnostics.Debug.WriteLine($"=== VALIDATION RESPONSE ===");
            System.Diagnostics.Debug.WriteLine(validationText);
            System.Diagnostics.Debug.WriteLine($"=== END VALIDATION ===");
            
            // Parse validation response
            var validationLines = validationText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool? currentValid = null;
            string currentReason = "";
            int resultIndex = 0;
            
            foreach (var line in validationLines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("VALID:", StringComparison.OrdinalIgnoreCase))
                {
                    var validStr = trimmed.Substring(6).Trim().ToUpperInvariant();
                    currentValid = validStr.Contains("YES");
                }
                else if (trimmed.StartsWith("REASON:", StringComparison.OrdinalIgnoreCase))
                {
                    currentReason = trimmed.Substring(7).Trim();
                }
                else if (trimmed == "---" && currentValid.HasValue)
                {
                    // Find the corresponding result (skip Low confidence ones)
                    while (resultIndex < results.Count && results[resultIndex].Confidence == "Low")
                    {
                        // Auto-accept Low confidence issues (they're informational)
                        validatedResults.Add(results[resultIndex]);
                        resultIndex++;
                    }
                    
                    if (resultIndex < results.Count)
                    {
                        if (currentValid.Value)
                        {
                            validatedResults.Add(results[resultIndex]);
                            System.Diagnostics.Debug.WriteLine($"✓ VALIDATED: {results[resultIndex].FilePath}:{results[resultIndex].LineNumber} - {currentReason}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"✗ REJECTED: {results[resultIndex].FilePath}:{results[resultIndex].LineNumber} - {currentReason}");
                        }
                        resultIndex++;
                    }
                    
                    currentValid = null;
                    currentReason = "";
                }
            }
            
            // Add any remaining Low confidence issues
            while (resultIndex < results.Count)
            {
                if (results[resultIndex].Confidence == "Low")
                {
                    validatedResults.Add(results[resultIndex]);
                }
                resultIndex++;
            }
            
            return validatedResults;
        }
    }
}
