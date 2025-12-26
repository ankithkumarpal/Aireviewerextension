using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.Enum;
using AiReviewer.Shared.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiReviewer.Shared.Services
{
    public class AiReviewService
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName;
        private readonly ILogger _logger;
        private string? _currentRepositoryPath;
        private TeamLearningApiClient? _teamApiClient;
        private StandardsService? _standardsService;

        /// <summary>
        /// Creates a new AiReviewService with optional logging.
        /// </summary>
        /// <param name="endpoint">Azure OpenAI endpoint URL</param>
        /// <param name="apiKey">Azure OpenAI API key</param>
        /// <param name="deploymentName">Azure OpenAI deployment name</param>
        /// <param name="logger">Optional logger instance. If null, logging is disabled.</param>
        public AiReviewService(string endpoint, string apiKey, string deploymentName, ILogger? logger = null)
        {
            _endpoint = endpoint;
            _apiKey = apiKey;
            _deploymentName = deploymentName;
            _logger = logger ?? NullLogger<AiReviewService>.Instance;
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
        /// Sets NNF standards directly (for server-side use without HTTP calls)
        /// </summary>
        public void SetNnfStandards(StagebotConfig standards)
        {
            _directNnfStandards = standards;
        }

        /// <summary>
        /// Sets learned patterns context directly (for server-side use without HTTP calls)
        /// </summary>
        public void SetLearnedPatterns(string patternsContext)
        {
            _directLearnedPatternsContext = patternsContext;
        }

        private StagebotConfig? _directNnfStandards;
        private string? _directLearnedPatternsContext;

        /// <summary>
        /// Reviews code with streaming progress updates for better UX
        /// Uses optimized prompt structure: System (cached) + User (dynamic)
        /// </summary>
        /// <param name="patches">List of code patches to review</param>
        /// <param name="config">Configuration for the review</param>
        /// <param name="repositoryPath">Path to the repository</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>List of review results</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
        public async Task<List<ReviewResult>> ReviewCodeWithStreamingAsync(
            List<Patch> patches, 
            StagebotConfig config, 
            string repositoryPath,
            Action<ReviewProgressUpdate> onProgress,
            CancellationToken cancellationToken = default)
        {
            _currentRepositoryPath = repositoryPath;
            
            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();
            
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

            // Get merged config: DirectStandards > StandardsService > provided config > EmbeddedStandards
            var effectiveConfig = config;
            
            // Use direct standards if set (server-side scenario - no HTTP needed)
            if (_directNnfStandards != null)
            {
                effectiveConfig = MergeConfigs(_directNnfStandards, config);
                onProgress?.Invoke(new ReviewProgressUpdate
                {
                    Type = ReviewProgressType.BuildingPrompt,
                    Message = $"Loaded {effectiveConfig.Checks?.Count ?? 0} checks from standards..."
                });
            }
            else if (_standardsService != null)
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
                    _logger.LogWarning(ex, "Standards fetch failed, using provided config");
                }
            }
            
            // Fallback to embedded standards if no checks configured
            if (effectiveConfig?.Checks == null || !effectiveConfig.Checks.Any())
            {
                _logger.LogDebug("No checks in config, using EmbeddedStandards");
                effectiveConfig = EmbeddedStandards.GetDefaults();
            }
            
            // Log effective checks for debugging
            _logger.LogDebug("Effective config has {CheckCount} checks", effectiveConfig.Checks?.Count ?? 0);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                foreach (var check in effectiveConfig.Checks?.Take(5) ?? new List<Check>())
                {
                    _logger.LogTrace("  Check: {CheckId} - {Description}", check.Id, check.Description);
                }
            }

            // AI LEARNING: Get patterns learned from user feedback
            // Use direct patterns if set (server-side scenario - no HTTP needed)
            string? learnedPatternsContext = _directLearnedPatternsContext;
            
            if (string.IsNullOrEmpty(learnedPatternsContext))
            {
                try
                {
                    if (!string.IsNullOrEmpty(repositoryPath) && _teamApiClient != null)
                    {
                        var patternAnalyzer = new PatternAnalyzer(repositoryPath, _teamApiClient);
                        
                        // Get relevant few-shot examples from Azure
                        var fileExtension = patches.FirstOrDefault()?.FilePath != null 
                            ? Path.GetExtension(patches.First().FilePath)?.ToLowerInvariant() 
                            : ".cs";
                        
                        var examples = await patternAnalyzer.GetPatternsAsync(fileExtension ?? ".cs", maxPatterns: 15, minAccuracy: 35.0).ConfigureAwait(false);
                        
                        if (examples.Count > 0)
                        {
                            learnedPatternsContext = patternAnalyzer.FormatExamplesForPrompt(examples);
                            _logger.LogDebug("Injected {PatternCount} learned patterns into prompt", examples.Count);
                            
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
                    _logger.LogWarning(ex, "Could not load learned patterns");
                }
            }
            else
            {
                _logger.LogDebug("Using direct learned patterns context");
            }

            // Build optimized prompts (System = cached, User = dynamic with file context)
            var userPrompt = ChecklistProvider.BuildUserPrompt(patches, effectiveConfig, repositoryPath, learnedPatternsContext);
            var estimatedTokens = ChecklistProvider.EstimateTokens(SystemPrompt.ReviewInstructions + userPrompt);
            
            // Log prompt for debugging
            _logger.LogDebug("Estimated tokens: {TotalTokens} (System: {SystemTokens}, User: {UserTokens})", 
                estimatedTokens, SystemPrompt.ReviewInstructions.Length/4, userPrompt.Length/4);
            _logger.LogTrace("User prompt preview: {PromptPreview}...", userPrompt.Substring(0, Math.Min(500, userPrompt.Length)));

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

            // Check for cancellation before making API call
            cancellationToken.ThrowIfCancellationRequested();

            // Use streaming API
            var responseBuilder = new StringBuilder();
            var streamingOptions = new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            };

            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, streamingOptions).WithCancellation(cancellationToken))
            {
                // Check for cancellation during streaming
                cancellationToken.ThrowIfCancellationRequested();
                
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

            _logger.LogInformation("Streaming review complete: found {IssueCount} issues", results.Count);
            return results;
        }

        /// <summary>
        /// Reviews code (non-streaming version for backward compatibility)
        /// Uses optimized prompt structure: System (cached) + User (dynamic)
        /// </summary>
        /// <param name="patches">List of code patches to review</param>
        /// <param name="config">Configuration for the review</param>
        /// <param name="repositoryPath">Path to the repository</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>List of review results</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
        public async Task<List<ReviewResult>> ReviewCodeAsync(
            List<Patch> patches, 
            StagebotConfig config, 
            string repositoryPath = null,
            CancellationToken cancellationToken = default)
        {
            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();
            
            // Store repository path for learning system
            _currentRepositoryPath = repositoryPath;
            
            // Log review start
            _logger.LogInformation("Starting AI review of {FileCount} file(s)", patches.Count);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var p in patches)
                {
                    _logger.LogDebug("  File: {FilePath} with {HunkCount} hunk(s)", p.FilePath, p.Hunks.Count);
                }
            }
            
            var client = new AzureOpenAIClient(new Uri(_endpoint), new ApiKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_deploymentName);
            
            // Get merged config: DirectStandards > StandardsService > provided config > EmbeddedStandards
            var effectiveConfig = config;
            
            // Use direct standards if set (server-side scenario - no HTTP needed)
            if (_directNnfStandards != null)
            {
                effectiveConfig = MergeConfigs(_directNnfStandards, config);
                _logger.LogDebug("Using direct standards: {CheckCount} checks", effectiveConfig.Checks?.Count ?? 0);
            }
            else if (_standardsService != null)
            {
                try
                {
                    effectiveConfig = await _standardsService.GetMergedConfigAsync(repositoryPath);
                    _logger.LogDebug("Using merged config: {CheckCount} checks", effectiveConfig.Checks?.Count ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Standards fetch failed");
                }
            }
            
            // Fallback to embedded standards if no checks configured
            if (effectiveConfig?.Checks == null || !effectiveConfig.Checks.Any())
            {
                _logger.LogDebug("No checks in config, using EmbeddedStandards");
                effectiveConfig = EmbeddedStandards.GetDefaults();
            }

            // AI LEARNING: Get patterns learned from user feedback
            // Use direct patterns if set (server-side scenario - no HTTP needed)
            string? learnedPatternsContext = _directLearnedPatternsContext;
            
            if (string.IsNullOrEmpty(learnedPatternsContext))
            {
                try
                {
                    if (!string.IsNullOrEmpty(repositoryPath) && _teamApiClient != null)
                    {
                        var patternAnalyzer = new PatternAnalyzer(repositoryPath, _teamApiClient);
                        
                        // Get relevant few-shot examples from Azure
                        var fileExtension = patches.FirstOrDefault()?.FilePath != null 
                            ? Path.GetExtension(patches.First().FilePath)?.ToLowerInvariant() 
                            : ".cs";
                        
                        var examples = await patternAnalyzer.GetPatternsAsync(fileExtension ?? ".cs", maxPatterns: 15, minAccuracy: 35.0).ConfigureAwait(false);
                        
                        if (examples.Count > 0)
                        {
                            learnedPatternsContext = patternAnalyzer.FormatExamplesForPrompt(examples);
                            _logger.LogDebug("Injected {PatternCount} learned patterns into prompt", examples.Count);
                        }
                    }
                }
                catch (Exception ex) 
                { 
                    _logger.LogWarning(ex, "Could not load learned patterns");
                }
            }
            else
            {
                _logger.LogDebug("Using direct learned patterns context");
            }

            // Build optimized prompts (with file context for better AI understanding)
            var userPrompt = ChecklistProvider.BuildUserPrompt(patches, effectiveConfig, repositoryPath, learnedPatternsContext);
            var estimatedTokens = ChecklistProvider.EstimateTokens(SystemPrompt.ReviewInstructions + userPrompt);

            // Log prompt sizes
            _logger.LogDebug("System prompt: {SystemChars} chars (~{SystemTokens} tokens, CACHED)", 
                SystemPrompt.ReviewInstructions.Length, SystemPrompt.ReviewInstructions.Length/4);
            _logger.LogDebug("User prompt: {UserChars} chars (~{UserTokens} tokens)", 
                userPrompt.Length, userPrompt.Length/4);
            _logger.LogDebug("Total estimated: ~{TotalTokens} tokens", estimatedTokens);
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt.ReviewInstructions),  // Cached by Azure OpenAI
                new UserChatMessage(userPrompt)  // Dynamic per request
            };

            // Check for cancellation before making API call
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Calling Azure OpenAI API...");
            var completion = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            }, cancellationToken);

            var reviewText = completion.Value.Content[0].Text;
            _logger.LogDebug("API call completed successfully");

            // Log AI response
            _logger.LogDebug("AI Response Length: {ResponseLength}", reviewText?.Length ?? 0);
            _logger.LogTrace("Full AI Response: {Response}", reviewText);

            // Parse the AI response into structured results
            var results = ParseReviewResponse(reviewText, patches);
            
            // Log parsing result
            _logger.LogDebug("Parsed {IssueCount} issues from AI response", results.Count);
            if (results.Count == 0)
            {
                _logger.LogWarning("No issues parsed! Check if AI response format matches expected format");
            }
            else if (_logger.IsEnabled(LogLevel.Trace))
            {
                foreach (var r in results)
                {
                    _logger.LogTrace("  Issue: {FilePath}:{LineNumber} - {Severity} - {Issue}", 
                        r.FilePath, r.LineNumber, r.Severity, r.Issue);
                }
            }
            
            // Add code snippets to results
            foreach (var result in results)
            {
                result.CodeSnippet = ExtractCodeSnippet(patches, result.FilePath, result.LineNumber);
            }
            
            _logger.LogInformation("AI review complete: found {IssueCount} issues", results.Count);
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
                    else if (trimmed.StartsWith("CHECKID:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("**CHECKID:**", StringComparison.OrdinalIgnoreCase))
                    {
                        var checkId = trimmed;
                        if (checkId.StartsWith("**CHECKID:**"))
                            checkId = checkId.Substring(12);
                        else
                            checkId = checkId.Substring(8);
                        checkId = checkId.Trim('*', ' ').ToLowerInvariant();
                        
                        if (!string.IsNullOrEmpty(checkId) && checkId != "none")
                        {
                            current.CheckId = checkId;
                            // Determine source based on check ID prefix
                            if (checkId.StartsWith("nnf-"))
                                current.RuleSource = "NNF";
                            else if (checkId.StartsWith("team-"))
                                current.RuleSource = "Team";
                            else if (checkId.StartsWith("repo-"))
                                current.RuleSource = "Repo";
                            else
                                current.RuleSource = "Repo"; // Default non-prefixed IDs to Repo
                        }
                        else
                        {
                            current.RuleSource = "AI";
                        }
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
        
        /// <summary>
        /// Merges direct standards with provided config.
        /// Direct standards take priority, but config can provide overrides.
        /// </summary>
        private StagebotConfig MergeConfigs(StagebotConfig directStandards, StagebotConfig? providedConfig)
        {
            // If no provided config, use direct standards as-is
            if (providedConfig == null)
            {
                return directStandards;
            }
            
            // Create merged config starting from direct standards
            var merged = new StagebotConfig
            {
                Version = directStandards.Version,
                Checks = directStandards.Checks?.ToList() ?? new List<AiReviewer.Shared.Models.Check>(),
                PrChecks = directStandards.PrChecks?.ToList() ?? new List<AiReviewer.Shared.Models.PrCheck>(),
                IncludePaths = directStandards.IncludePaths?.ToList() ?? new List<string>(),
                ExcludePaths = directStandards.ExcludePaths?.ToList() ?? new List<string>(),
                InheritCentralStandards = providedConfig.InheritCentralStandards
            };
            
            // Add any custom checks from provided config
            if (providedConfig.Checks != null)
            {
                foreach (var check in providedConfig.Checks)
                {
                    // Add check if not already present (by Id)
                    if (!merged.Checks.Any(c => c.Id == check.Id))
                    {
                        merged.Checks.Add(check);
                    }
                }
            }
            
            // Merge PR checks
            if (providedConfig.PrChecks != null)
            {
                foreach (var prCheck in providedConfig.PrChecks)
                {
                    if (!merged.PrChecks.Any(c => c.Id == prCheck.Id))
                    {
                        merged.PrChecks.Add(prCheck);
                    }
                }
            }
            
            // Merge include/exclude paths
            if (providedConfig.IncludePaths != null)
            {
                foreach (var path in providedConfig.IncludePaths)
                {
                    if (!merged.IncludePaths.Contains(path))
                    {
                        merged.IncludePaths.Add(path);
                    }
                }
            }
            
            if (providedConfig.ExcludePaths != null)
            {
                foreach (var path in providedConfig.ExcludePaths)
                {
                    if (!merged.ExcludePaths.Contains(path))
                    {
                        merged.ExcludePaths.Add(path);
                    }
                }
            }
            
            return merged;
        }
    }
}
