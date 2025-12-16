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
using AiReviewer.Shared.Models;
using AiReviewer.Shared.Enum;
using AiReviewer.Shared.Prompts;

namespace AiReviewer.Shared.Services
{
    public class AiReviewService
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName;
        private string _currentRepositoryPath;
        private TeamLearningApiClient _teamApiClient;
        private StandardsService _standardsService;

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
        /// Sets the Standards service for fetching NNF standards
        /// </summary>
        public void SetStandardsService(StandardsService service)
        {
            _standardsService = service;
        }

        /// <summary>
        /// Reviews code with streaming progress updates for better UX
        /// Uses optimized prompt structure: System (cached) + User (dynamic)
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

            // Get merged config: StandardsService > provided config > EmbeddedStandards
            var effectiveConfig = config;
            if (_standardsService != null)
            {
                try
                {
                    effectiveConfig = await _standardsService.GetMergedConfigAsync(repositoryPath);
                    onProgress?.Invoke(new ReviewProgressUpdate
                    {
                        Type = ReviewProgressType.BuildingPrompt,
                        Message = $"Loaded {effectiveConfig.Checks?.Count ?? 0} checks from standards..."
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiReview] Standards fetch failed, using provided config: {ex.Message}");
                }
            }
            
            // Fallback to embedded standards if no checks configured
            if (effectiveConfig?.Checks == null || !effectiveConfig.Checks.Any())
            {
                System.Diagnostics.Debug.WriteLine("[AiReview] No checks in config, using EmbeddedStandards");
                effectiveConfig = EmbeddedStandards.GetDefaults();
            }
            
            // Log effective checks for debugging
            System.Diagnostics.Debug.WriteLine($"[AiReview] Effective config has {effectiveConfig.Checks?.Count ?? 0} checks:");
            foreach (var check in effectiveConfig.Checks?.Take(5) ?? new List<Check>())
            {
                System.Diagnostics.Debug.WriteLine($"  - {check.Id}: {check.Description}");
            }

            // AI LEARNING: Get patterns learned from user feedback
            string learnedPatternsContext = null;
            try
            {
                if (!string.IsNullOrEmpty(repositoryPath) && _teamApiClient != null)
                {
                    var patternAnalyzer = new PatternAnalyzer(repositoryPath, _teamApiClient);
                    
                    // Get relevant few-shot examples from Azure
                    var fileExtension = patches.FirstOrDefault()?.FilePath != null 
                        ? Path.GetExtension(patches.First().FilePath)?.ToLowerInvariant() 
                        : ".cs";
                    
                    var examples = patternAnalyzer.GetRelevantPatterns(fileExtension ?? ".cs", maxPatterns: 15, minAccuracy: 35.0);
                    
                    if (examples.Count > 0)
                    {
                        learnedPatternsContext = patternAnalyzer.FormatExamplesForPrompt(examples);
                        System.Diagnostics.Debug.WriteLine($"[AiReview] Injected {examples.Count} learned patterns into prompt");
                        
                        onProgress?.Invoke(new ReviewProgressUpdate
                        {
                            Type = ReviewProgressType.BuildingPrompt,
                            Message = $"Loaded {examples.Count} learned patterns from team feedback..."
                        });
                    }
                }
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"[AiReview] Warning: Could not load learned patterns: {ex.Message}");
            }

            // Build optimized prompts (System = cached, User = dynamic with file context)
            var userPrompt = ChecklistProvider.BuildUserPrompt(patches, effectiveConfig, repositoryPath, learnedPatternsContext);
            var estimatedTokens = ChecklistProvider.EstimateTokens(SystemPrompt.ReviewInstructions + userPrompt);
            
            // Log prompt for debugging
            System.Diagnostics.Debug.WriteLine($"[AiReview] Estimated tokens: {estimatedTokens} (System: {SystemPrompt.ReviewInstructions.Length/4}, User: {userPrompt.Length/4})");
            System.Diagnostics.Debug.WriteLine($"[AiReview] User prompt preview (first 500 chars):\n{userPrompt.Substring(0, Math.Min(500, userPrompt.Length))}...");

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt.ReviewInstructions),  // Cached by Azure OpenAI
                new UserChatMessage(userPrompt)  // Dynamic per request
            };

            // Report: Calling AI
            onProgress?.Invoke(new ReviewProgressUpdate
            {
                Type = ReviewProgressType.CallingAI,
                Message = $"Sending to AI for review (~{estimatedTokens} tokens)..."
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
        /// Uses optimized prompt structure: System (cached) + User (dynamic)
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
            
            // Get merged config: StandardsService > provided config > EmbeddedStandards
            var effectiveConfig = config;
            if (_standardsService != null)
            {
                try
                {
                    effectiveConfig = await _standardsService.GetMergedConfigAsync(repositoryPath);
                    System.Diagnostics.Debug.WriteLine($"[AiReview] Using merged config: {effectiveConfig.Checks?.Count ?? 0} checks");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AiReview] Standards fetch failed: {ex.Message}");
                }
            }
            
            // Fallback to embedded standards if no checks configured
            if (effectiveConfig?.Checks == null || !effectiveConfig.Checks.Any())
            {
                System.Diagnostics.Debug.WriteLine("[AiReview] No checks in config, using EmbeddedStandards");
                effectiveConfig = EmbeddedStandards.GetDefaults();
            }

            // AI LEARNING: Get patterns learned from user feedback
            string learnedPatternsContext = null;
            try
            {
                if (!string.IsNullOrEmpty(repositoryPath) && _teamApiClient != null)
                {
                    var patternAnalyzer = new PatternAnalyzer(repositoryPath, _teamApiClient);
                    
                    // Get relevant few-shot examples from Azure
                    var fileExtension = patches.FirstOrDefault()?.FilePath != null 
                        ? Path.GetExtension(patches.First().FilePath)?.ToLowerInvariant() 
                        : ".cs";
                    
                    var examples = patternAnalyzer.GetRelevantPatterns(fileExtension ?? ".cs", maxPatterns: 15, minAccuracy: 35.0);
                    
                    if (examples.Count > 0)
                    {
                        learnedPatternsContext = patternAnalyzer.FormatExamplesForPrompt(examples);
                        System.Diagnostics.Debug.WriteLine($"[AiReview] Injected {examples.Count} learned patterns into prompt");
                    }
                }
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"[AiReview] Warning: Could not load learned patterns: {ex.Message}");
            }

            // Build optimized prompts (with file context for better AI understanding)
            var userPrompt = ChecklistProvider.BuildUserPrompt(patches, effectiveConfig, repositoryPath, learnedPatternsContext);
            var estimatedTokens = ChecklistProvider.EstimateTokens(SystemPrompt.ReviewInstructions + userPrompt);

            // Debug: Log prompt sizes
            System.Diagnostics.Debug.WriteLine($"System prompt: {SystemPrompt.ReviewInstructions.Length} chars (~{SystemPrompt.ReviewInstructions.Length/4} tokens, CACHED)");
            System.Diagnostics.Debug.WriteLine($"User prompt: {userPrompt.Length} chars (~{userPrompt.Length/4} tokens)");
            System.Diagnostics.Debug.WriteLine($"Total estimated: ~{estimatedTokens} tokens");
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt.ReviewInstructions),  // Cached by Azure OpenAI
                new UserChatMessage(userPrompt)  // Dynamic per request
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
            
            // PASS 2: Validate the fixes (commented this since it was filtering too aggressively result in bad review suggestion)
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

        // Todo : For Better suggestion this check needs to be used before returning review results.
        // Validate the suggested fixes by checking if they actually resolve the issues
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
