using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using AiReviewer.Shared.Enum;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.Services;
using AiReviewer.Shared.StaticHelper;
using AiReviewer.VSIX.Configuration;
using AiReviewer.VSIX.Services;

namespace AiReviewer.VSIX.ToolWindows
{
    public partial class AiReviewerToolWindowControl : UserControl
    {
        // Store all results for filtering
        private List<ReviewResult> _allResults = new List<ReviewResult>();
        private string _currentFilter = "All";

        public AiReviewerToolWindowControl()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[AI Reviewer] Initializing control...");
                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("[AI Reviewer] InitializeComponent complete");
                
                // Track when control is loaded and rendered
                this.Loaded += (s, e) => 
                {
                    System.Diagnostics.Debug.WriteLine("[AI Reviewer] Control LOADED event fired");
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing control: {ex.Message}");
                throw new Exception($"Failed to initialize AI Reviewer control: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Triggers the review programmatically (called from menu command)
        /// </summary>
        public void TriggerReview()
        {
            System.Diagnostics.Debug.WriteLine("[AI Reviewer] TriggerReview called");
            
            // Only trigger if not already reviewing
            if (ReviewButton.IsEnabled)
            {
                // Simulate button click
                ReviewButton_Click(ReviewButton, new RoutedEventArgs());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[AI Reviewer] Review already in progress");
            }
        }

        /// <summary>
        /// Shows streaming progress in the UI
        /// </summary>
        public void ShowStreamingProgress(ReviewProgressUpdate progress)
        {
            System.Diagnostics.Debug.WriteLine($"[Progress] {progress.Type}: {progress.Message}");
            
            // Must be called on UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ShowStreamingProgress(progress)));
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Progress] Setting panel visible");
            StreamingProgressPanel.Visibility = Visibility.Visible;

            switch (progress.Type)
            {
                case ReviewProgressType.Started:
                    StreamingStatusIcon.Text = "🚀";
                    StreamingStatusText.Text = progress.Message;
                    StreamingProgressBar.IsIndeterminate = true;
                    StreamingDetailsText.Text = $"Files to review: {progress.TotalFiles}";
                    break;

                case ReviewProgressType.BuildingPrompt:
                    StreamingStatusIcon.Text = "📝";
                    StreamingStatusText.Text = progress.Message;
                    StreamingDetailsText.Text = "Reading file context and building AI prompt...";
                    break;

                case ReviewProgressType.CallingAI:
                    StreamingStatusIcon.Text = "🤖";
                    StreamingStatusText.Text = progress.Message;
                    StreamingDetailsText.Text = "Waiting for Azure OpenAI response...";
                    break;

                case ReviewProgressType.Streaming:
                    StreamingStatusIcon.Text = "📡";
                    StreamingStatusText.Text = progress.Message;
                    StreamingDetailsText.Text = $"Tokens received: ~{progress.ProcessedTokens}";
                    break;

                case ReviewProgressType.ParsingResults:
                    StreamingStatusIcon.Text = "🔍";
                    StreamingStatusText.Text = progress.Message;
                    StreamingDetailsText.Text = "Extracting issues from AI response...";
                    break;

                case ReviewProgressType.Completed:
                    StreamingProgressPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// Hides the streaming progress panel
        /// </summary>
        public void HideStreamingProgress()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => HideStreamingProgress()));
                return;
            }
            System.Diagnostics.Debug.WriteLine("[Progress] Hiding progress panel");
            StreamingProgressPanel.Visibility = Visibility.Collapsed;
            
            // Reset to default state
            StreamingStatusIcon.Text = "⏳";
            StreamingStatusText.Text = "Waiting for review...";
            StreamingDetailsText.Text = "Click 'Review Staged Changes' to start";
        }

        public void ShowResults(List<ReviewResult> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Hide progress panel when showing results
            StreamingProgressPanel.Visibility = Visibility.Collapsed;

            // Store all results for filtering
            _allResults = results ?? new List<ReviewResult>();
            _currentFilter = "All";

            // Reset filter buttons
            FilterAllButton.IsChecked = true;
            FilterHighButton.IsChecked = false;
            FilterMediumButton.IsChecked = false;
            FilterLowButton.IsChecked = false;

            // Show filter panel if there are results
            FilterPanel.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Display results
            var viewModels = results.Select(r => new ReviewResultViewModel(r)).ToList();
            ResultsList.ItemsSource = viewModels;

            var highCount = results.Count(r => r.Severity == "High");
            var mediumCount = results.Count(r => r.Severity == "Medium");
            var lowCount = results.Count(r => r.Severity == "Low");

            if (results.Count == 0)
            {
                SummaryText.Text = "✅ No issues found! Your code looks great.";
            }
            else
            {
                SummaryText.Text = $"Found {results.Count} issue(s): ";
                if (highCount > 0) SummaryText.Text += $" {highCount} High  ";
                if (mediumCount > 0) SummaryText.Text += $"{mediumCount} Medium  ";
                if (lowCount > 0) SummaryText.Text += $"{lowCount} Low";
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is System.Windows.Controls.Primitives.ToggleButton clickedButton)
            {
                var filter = clickedButton.Tag?.ToString() ?? "All";

                // Update toggle states (radio-button behavior)
                FilterAllButton.IsChecked = filter == "All";
                FilterHighButton.IsChecked = filter == "High";
                FilterMediumButton.IsChecked = filter == "Medium";
                FilterLowButton.IsChecked = filter == "Low";

                _currentFilter = filter;
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IEnumerable<ReviewResult> filteredResults;

            if (_currentFilter == "All")
            {
                filteredResults = _allResults;
            }
            else
            {
                filteredResults = _allResults.Where(r => 
                    string.Equals(r.Severity, _currentFilter, StringComparison.OrdinalIgnoreCase));
            }

            var viewModels = filteredResults.Select(r => new ReviewResultViewModel(r)).ToList();
            ResultsList.ItemsSource = viewModels;

            // Update summary to show filtered count
            var totalCount = _allResults.Count;
            var filteredCount = viewModels.Count;

            if (_currentFilter == "All")
            {
                var highCount = _allResults.Count(r => r.Severity == "High");
                var mediumCount = _allResults.Count(r => r.Severity == "Medium");
                var lowCount = _allResults.Count(r => r.Severity == "Low");

                SummaryText.Text = $"Found {totalCount} issue(s): ";
                if (highCount > 0) SummaryText.Text += $"🔴 {highCount} High  ";
                if (mediumCount > 0) SummaryText.Text += $"🟡 {mediumCount} Medium  ";
                if (lowCount > 0) SummaryText.Text += $"🟢 {lowCount} Low";
            }
            else
            {
                var severityEmoji = _currentFilter == "High" ? "🔴" : 
                                    _currentFilter == "Medium" ? "🟡" : "🟢";
                SummaryText.Text = $"Showing {filteredCount} of {totalCount} issues ({severityEmoji} {_currentFilter} only)";
            }
        }

        private async void ReviewButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== REVIEW BUTTON CLICKED ===");
            
            // Disable button immediately
            ReviewButton.IsEnabled = false;
            ReviewButton.Content = "⏳ Reviewing...";
            
            // Show progress panel RIGHT AWAY
            StreamingProgressPanel.Visibility = Visibility.Visible;
            StreamingStatusIcon.Text = "🚀";
            StreamingStatusText.Text = "Starting review...";
            StreamingProgressBar.IsIndeterminate = true;
            StreamingDetailsText.Text = "Analyzing your staged changes...";
            
            // FORCE the UI to render now before doing any work
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            System.Diagnostics.Debug.WriteLine("Progress panel shown, starting review...");
            
            try
            {
                await RunReviewWithStreamingAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: {ex}");
                SummaryText.Text = $"❌ Error: {ex.Message}";
                StreamingProgressPanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                ReviewButton.IsEnabled = true;
                ReviewButton.Content = "🔍 Review Staged Changes";
            }
        }

        private void UpdateProgress(string icon, string status, string details)
        {
            // Always invoke on UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(icon, status, details));
                return;
            }
            
            StreamingStatusIcon.Text = icon;
            StreamingStatusText.Text = status;
            StreamingDetailsText.Text = details;
        }

        private async Task RunReviewWithStreamingAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            UpdateProgress("📂", "Getting workspace...", "Finding solution or folder...");

            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
            string workingDir = null;

            if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                workingDir = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            else if (dte?.ActiveDocument?.Path != null)
                workingDir = dte.ActiveDocument.Path;

            if (string.IsNullOrEmpty(workingDir))
            {
                SummaryText.Text = "❌ Could not determine workspace. Open a solution or file.";
                StreamingProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateProgress("🔍", "Finding git repository...", workingDir);
            
            // Run git operations off UI thread
            var repo = await Task.Run(() => GitDiff.FindRepoRoot(workingDir));
            
            if (string.IsNullOrEmpty(repo))
            {
                SummaryText.Text = "❌ No git repository found.";
                StreamingProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateProgress("📋", "Getting staged changes...", "Running git diff...");
            
            // Run git diff off UI thread
            var (diff, patches) = await Task.Run(() => 
            {
                var d = GitDiff.GetStagedUnifiedDiff(repo);
                var p = GitDiff.ParseUnified(d);
                return (d, p);
            });

            if (patches.Count == 0)
            {
                SummaryText.Text = "❌ No staged changes. Use 'git add' first.";
                StreamingProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateProgress("📝", $"Found {patches.Count} file(s)...", "Loading configuration...");

            // Load config off UI thread
            var (cfg, cfgPath) = await Task.Run(() =>
            {
                var path = System.IO.Path.Combine(repo, ".config", "merlinBot", "PullRequestAssistant.yaml");
                var config = System.IO.File.Exists(path) ? MerlinConfigLoader.Load(path) : new MerlinConfig();
                return (config, path);
            });

            UpdateProgress("🔐", "Authenticating...", "Signing in to Azure AD...");

            // Use Azure AD authentication
            var apiClient = new TeamLearningApiClient(AppConfig.ApiUrl, 
                () => AzureAdAuthService.Instance.GetAccessTokenAsync());
            var aiConfig = await apiClient.GetAiConfigAsync();

            if (!aiConfig.IsValid)
            {
                SummaryText.Text = $"❌ AI service error: {aiConfig.Error}";
                StreamingProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateProgress("🤖", "AI is analyzing your code...", $"Reviewing {patches.Count} file(s) - this may take 10-30 seconds...");

            var aiService = new AiReviewService(aiConfig.AzureOpenAIEndpoint, aiConfig.AzureOpenAIKey, aiConfig.DeploymentName);
            if (AppConfig.EnableTeamLearning)
                aiService.SetTeamApiClient(apiClient);

            // Use streaming review with progress callback
            var results = await aiService.ReviewCodeWithStreamingAsync(patches, cfg, repo, progress =>
            {
                // Update UI on dispatcher
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    switch (progress.Type)
                    {
                        case ReviewProgressType.BuildingPrompt:
                            UpdateProgress("📝", "Building prompt...", "Gathering file context...");
                            break;
                        case ReviewProgressType.CallingAI:
                            UpdateProgress("📡", "Sending to AI...", "Waiting for response...");
                            break;
                        case ReviewProgressType.Streaming:
                            var charCount = progress.PartialResponse?.Length ?? 0;
                            UpdateProgress("💬", "Receiving response...", $"{charCount} characters received...");
                            break;
                        case ReviewProgressType.ParsingResults:
                            UpdateProgress("🔍", "Parsing results...", "Extracting issues from response...");
                            break;
                    }
                }));
            });

            foreach (var result in results)
                result.RepositoryPath = repo;

            // Hide progress, show results
            StreamingProgressPanel.Visibility = Visibility.Collapsed;
            ShowResults(results);
        }

        #region Learning Stats Panel

        private string _currentRepositoryPath;

        private void ShowStatsButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            // Get repository path
            _currentRepositoryPath = GetCurrentRepositoryPath();
            
            if (string.IsNullOrEmpty(_currentRepositoryPath))
            {
                MessageBox.Show(
                    "Could not determine repository path.\n\nPlease open a solution that is in a Git repository.",
                    "AI Code Reviewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Show stats panel
            LearningStatsPanel.Visibility = Visibility.Visible;
            RefreshLearningStats();
        }

        private void CloseStatsButton_Click(object sender, RoutedEventArgs e)
        {
            LearningStatsPanel.Visibility = Visibility.Collapsed;
        }

        private void RefreshLearningStats()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentRepositoryPath))
                {
                    AccuracyText.Text = "--%";
                    PatternsText.Text = "--";
                    FeedbackCountText.Text = "--";
                    HelpfulRateText.Text = "--%";
                    LastAnalyzedText.Text = "No repository detected";
                    return;
                }

                var patternAnalyzer = GetPatternAnalyzer(_currentRepositoryPath);
                var stats = patternAnalyzer.GetLearningStats();
                var feedbackManager = GetFeedbackManager(_currentRepositoryPath);
                var feedbackStats = feedbackManager.GetStats();

                // Update UI
                AccuracyText.Text = $"{stats.OverallAccuracy:F1}%";
                PatternsText.Text = stats.TotalPatterns.ToString();
                FeedbackCountText.Text = feedbackStats.TotalFeedback.ToString();
                HelpfulRateText.Text = $"{feedbackStats.AccuracyPercent:F1}%";

                // Build status text
                string statusText;
                if (feedbackStats.IsTeamStats)
                {
                    statusText = $"👥 Team Learning: {feedbackStats.UniqueContributors} contributors";
                    if (feedbackStats.TopContributors != null && feedbackStats.TopContributors.Count > 0)
                    {
                        var topNames = string.Join(", ", feedbackStats.TopContributors.Take(3).Select(c => c.Name));
                        statusText += $" • Top: {topNames}";
                    }
                }
                else if (stats.LastAnalyzed > DateTime.MinValue && stats.TotalPatterns > 0)
                {
                    var timeAgo = DateTime.UtcNow - stats.LastAnalyzed;
                    string timeText;
                    if (timeAgo.TotalMinutes < 1)
                        timeText = "just now";
                    else if (timeAgo.TotalHours < 1)
                        timeText = $"{(int)timeAgo.TotalMinutes}m ago";
                    else if (timeAgo.TotalDays < 1)
                        timeText = $"{(int)timeAgo.TotalHours}h ago";
                    else
                        timeText = $"{(int)timeAgo.TotalDays}d ago";
                    
                    statusText = $"📁 Local Learning • Last analyzed: {timeText}";
                }
                else
                {
                    // Team learning is always enabled with hardcoded config
                    statusText = "No feedback data yet. Rate some reviews to start learning!";
                }

                LastAnalyzedText.Text = statusText;

                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Stats refreshed: {stats.TotalPatterns} patterns, {stats.OverallAccuracy}% accuracy");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Error refreshing stats: {ex.Message}");
                AccuracyText.Text = "Error";
                PatternsText.Text = "--";
                FeedbackCountText.Text = "--";
                HelpfulRateText.Text = "--%";
                LastAnalyzedText.Text = $"Error loading stats: {ex.Message}";
            }
        }

        private string GetCurrentRepositoryPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                    
                    // Try to find git root
                    var currentDir = solutionDir;
                    while (!string.IsNullOrEmpty(currentDir))
                    {
                        if (Directory.Exists(Path.Combine(currentDir, ".git")))
                        {
                            return currentDir;
                        }
                        currentDir = Path.GetDirectoryName(currentDir);
                    }
                    
                    // Fallback to solution directory
                    return solutionDir;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Error getting repository path: {ex.Message}");
            }
            return null;
        }

        #endregion

        private void ApplyFixButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var button = sender as Button;
            var viewModel = button?.Tag as ReviewResultViewModel;
            if (viewModel != null)
            {
                CodeFixApplier.ApplyFix(viewModel.Result);
            }
        }

        private void ViewInEditorButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var button = sender as Button;
            var viewModel = button?.Tag as ReviewResultViewModel;
            if (viewModel == null)
                return;

            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte == null)
                {
                    System.Windows.MessageBox.Show(
                        "Could not access Visual Studio.",
                        "AI Code Reviewer",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                var filePath = FindFullPath(viewModel.Result.FilePath, viewModel.Result.RepositoryPath, dte);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    System.Windows.MessageBox.Show(
                        $"Could not find file: {viewModel.Result.FilePath}\n\nRepository: {viewModel.Result.RepositoryPath}\n\nTried searching in repository and solution folders.",
                        "AI Code Reviewer",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var window = dte.ItemOperations.OpenFile(filePath);
                var textDoc = window.Document.Object("TextDocument") as TextDocument;
                if (textDoc != null)
                {
                    textDoc.Selection.GotoLine(viewModel.Result.LineNumber, false);
                    textDoc.Selection.SelectLine();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error opening file:\\n{ex.Message}",
                    "AI Code Reviewer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private string FindFullPath(string relativePath, string repositoryPath, DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // First, try the repository path (most likely location)
            if (!string.IsNullOrEmpty(repositoryPath))
            {
                var repoFilePath = Path.Combine(repositoryPath, relativePath.Replace("/", "\\"));
                if (File.Exists(repoFilePath))
                    return repoFilePath;
            }

            // Fallback: try solution directory
            if (dte.Solution?.FullName != null)
            {
                var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                var fullPath = Path.Combine(solutionDir, relativePath.Replace("/", "\\"));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            var dte2 = dte as EnvDTE80.DTE2;
            if (dte2?.Solution != null)
            {
                foreach (Project project in dte2.Solution.Projects)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(project.FullName))
                        {
                            var projectDir = Path.GetDirectoryName(project.FullName);
                            var fullPath = Path.Combine(projectDir, relativePath.Replace("/", "\\"));
                            if (File.Exists(fullPath))
                                return fullPath;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        #region Feedback Handlers

        /// <summary>
        /// Gets the Team Learning API client if enabled in settings
        /// </summary>
        private TeamLearningApiClient? GetTeamApiClient()
        {
            // Use Azure AD authentication
            if (AppConfig.EnableTeamLearning)
            {
                return new TeamLearningApiClient(AppConfig.ApiUrl, 
                    () => AzureAdAuthService.Instance.GetAccessTokenAsync());
            }
            return null;
        }

        /// <summary>
        /// Gets the contributor name - uses Azure AD email or falls back to Windows username
        /// </summary>
        private string GetContributorName()
        {
            // Try to get email from Azure AD (if user is signed in)
            // For now, use Windows username - will be updated to use AAD claim
            return AppConfig.ContributorName;
        }

        private FeedbackManager GetFeedbackManager(string repositoryPath)
        {
            if (string.IsNullOrEmpty(repositoryPath))
            {
                throw new InvalidOperationException("Repository path is not available for this review result.");
            }
            
            var teamApiClient = GetTeamApiClient();
            if (teamApiClient == null)
            {
                throw new InvalidOperationException("Team Learning API is not configured. Please configure in Tools > Options > AI Reviewer.");
            }
            
            var contributorName = GetContributorName();
            return new FeedbackManager(repositoryPath, teamApiClient, contributorName);
        }

        private PatternAnalyzer GetPatternAnalyzer(string repositoryPath)
        {
            if (string.IsNullOrEmpty(repositoryPath))
            {
                throw new InvalidOperationException("Repository path is not available.");
            }
            
            var teamApiClient = GetTeamApiClient();
            if (teamApiClient == null)
            {
                throw new InvalidOperationException("Team Learning API is not configured. Please configure in Tools > Options > AI Reviewer.");
            }
            
            return new PatternAnalyzer(repositoryPath, teamApiClient);
        }

        private void HelpfulButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            var button = sender as Button;
            var viewModel = button?.Tag as ReviewResultViewModel;
            if (viewModel == null) return;

            try
            {
                var feedbackManager = GetFeedbackManager(viewModel.Result.RepositoryPath);
                var feedback = feedbackManager.CreateFeedback(
                    filePath: viewModel.FilePath,
                    lineNumber: viewModel.LineNumber,
                    issueDescription: viewModel.Issue,
                    severity: viewModel.Result.Severity,
                    rule: viewModel.Rule,
                    codeSnippet: viewModel.CodeSnippet,
                    wasHelpful: true,
                    userCorrection: null,
                    reason: null
                );

                feedbackManager.SaveFeedback(feedback);

                // Visual feedback
                button.Content = "✅ Thanks!";
                button.IsEnabled = false;
                DisableSiblingFeedbackButtons(button);

                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Feedback saved: Helpful for {viewModel.FilePath}:{viewModel.LineNumber}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Error saving feedback: {ex.Message}");
                MessageBox.Show(
                    $"Could not save feedback: {ex.Message}",
                    "AI Code Reviewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void NotHelpfulButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            var button = sender as Button;
            var viewModel = button?.Tag as ReviewResultViewModel;
            if (viewModel == null) return;

            // Show dialog to get reason
            var reason = PromptForReason("Why wasn't this review helpful?", 
                "Please help us improve by explaining why this suggestion wasn't useful:\n\n" +
                "• False positive (code is actually correct)\n" +
                "• Not relevant to my context\n" +
                "• Suggestion is unclear\n" +
                "• Other");

            try
            {
                var feedbackManager = GetFeedbackManager(viewModel.Result.RepositoryPath);
                var feedback = feedbackManager.CreateFeedback(
                    filePath: viewModel.FilePath,
                    lineNumber: viewModel.LineNumber,
                    issueDescription: viewModel.Issue,
                    severity: viewModel.Result.Severity,
                    rule: viewModel.Rule,
                    codeSnippet: viewModel.CodeSnippet,
                    wasHelpful: false,
                    userCorrection: null,
                    reason: reason
                );

                feedbackManager.SaveFeedback(feedback);

                // Visual feedback
                button.Content = "✅ Noted";
                button.IsEnabled = false;
                DisableSiblingFeedbackButtons(button);

                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Feedback saved: Not Helpful for {viewModel.FilePath}:{viewModel.LineNumber}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Error saving feedback: {ex.Message}");
                MessageBox.Show(
                    $"Could not save feedback: {ex.Message}",
                    "AI Code Reviewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CorrectItButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            var button = sender as Button;
            var viewModel = button?.Tag as ReviewResultViewModel;
            if (viewModel == null) return;

            // Show dialog to get user's correction
            var correction = PromptForCorrection(
                "Provide Your Correction",
                $"AI suggested:\n{viewModel.Suggestion}\n\nWhat should the correct advice be?",
                viewModel.Result.FixedCode);

            if (string.IsNullOrWhiteSpace(correction))
            {
                return; // User cancelled
            }

            try
            {
                var feedbackManager = GetFeedbackManager(viewModel.Result.RepositoryPath);
                var feedback = feedbackManager.CreateFeedback(
                    filePath: viewModel.FilePath,
                    lineNumber: viewModel.LineNumber,
                    issueDescription: viewModel.Issue,
                    severity: viewModel.Result.Severity,
                    rule: viewModel.Rule,
                    codeSnippet: viewModel.CodeSnippet,
                    wasHelpful: false,
                    userCorrection: correction,
                    reason: "User provided correction"
                );

                feedbackManager.SaveFeedback(feedback);

                // Visual feedback
                button.Content = "✅ Saved!";
                button.IsEnabled = false;
                DisableSiblingFeedbackButtons(button);

                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Correction saved for {viewModel.FilePath}:{viewModel.LineNumber}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI Reviewer] Error saving correction: {ex.Message}");
                MessageBox.Show(
                    $"Could not save correction: {ex.Message}",
                    "AI Code Reviewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DisableSiblingFeedbackButtons(Button clickedButton)
        {
            // Find parent StackPanel and disable other feedback buttons
            var parent = clickedButton.Parent as StackPanel;
            if (parent == null) return;

            foreach (var child in parent.Children)
            {
                if (child is Button btn && btn != clickedButton)
                {
                    btn.IsEnabled = false;
                    // Style handles disabled appearance - no need to change opacity
                }
            }
        }

        private string PromptForReason(string title, string message)
        {
            // Simple input dialog
            var dialog = new System.Windows.Window
            {
                Title = title,
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48))
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Submit", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Skip", Width = 80, IsCancel = true };

            string result = null;
            okButton.Click += (s, args) => { result = textBox.Text; dialog.Close(); };
            cancelButton.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();

            return result;
        }

        private string PromptForCorrection(string title, string message, string prefill)
        {
            var dialog = new System.Windows.Window
            {
                Title = title,
                Width = 500,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48))
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox
            {
                Text = prefill ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Save Correction", Width = 120, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            string result = null;
            okButton.Click += (s, args) => { result = textBox.Text; dialog.Close(); };
            cancelButton.Click += (s, args) => dialog.Close();

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();

            return result;
        }

        #endregion
    }

    public class ReviewResultViewModel
    {
        public ReviewResult Result { get; }

        public ReviewResultViewModel(ReviewResult result)
        {
            Result = result;
        }

        public string FilePath => Result.FilePath;
        public int LineNumber => Result.LineNumber;
        public string Issue => Result.Issue;
        public string Suggestion => Result.Suggestion;
        public string CodeSnippet => Result.CodeSnippet;
        public string FixedCode => Result.FixedCode;
        public string Rule => string.IsNullOrEmpty(Result.Rule) ? "Code Quality" : Result.Rule;
        public string Confidence => string.IsNullOrEmpty(Result.Confidence) ? "Medium" : Result.Confidence;

        public string SeverityIcon => Result.Severity == "High" ? "🔴" :
                                      Result.Severity == "Medium" ? "🟡" : "🟢";
        
        public string ConfidenceIcon => Confidence == "High" ? "✓✓✓" :
                                         Confidence == "Medium" ? "✓✓" : "✓";

        public string RuleIcon => Result.Rule switch
        {
            "Security" => "🔒",
            "Performance" => "⚡",
            "Reliability" => "🛡️",
            "Code Quality" => "🎯",
            "Best Practices" => "📝",
            "Documentation" => "✍️",
            _ => "📋"
        };

        public Visibility CodeSnippetVisibility => string.IsNullOrEmpty(CodeSnippet) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility FixedCodeVisibility => string.IsNullOrEmpty(FixedCode) ? Visibility.Collapsed : Visibility.Visible;
    }
}
