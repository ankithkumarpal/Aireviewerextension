using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using AiReviewer.Shared;
using AiReviewer.Shared.Models;
using AiReviewer.Shared.Services;

namespace AiReviewer.VSIX
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
                InitializeComponent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing control: {ex.Message}");
                throw new Exception($"Failed to initialize AI Reviewer control: {ex.Message}", ex);
            }
        }

        public void ShowResults(List<ReviewResult> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

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

        private void ReviewButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () => await ReviewCommand.RunAsync());
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

        private void RefreshStatsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshLearningStats();
        }

        private void ClearFeedbackButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Feedback data is stored in Azure Team Learning.\n\nTo clear team data, contact your administrator or use the Azure Portal.",
                "Clear Feedback Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            // ═══════════════════════════════════════════════════════════════════
            // TESTING MODE: Hardcoded values for testing
            // TODO: Remove this block after testing and use Options page instead
            // ═══════════════════════════════════════════════════════════════════
            const bool TESTING_MODE = true; // Set to false to use Options page
            
            if (TESTING_MODE)
            {
                const string TEST_API_URL = "https://ai-reviewer-teamlearning-apc4dvfhgxaze3h9.eastus-01.azurewebsites.net/api";
                const string TEST_API_KEY = "TeamLearning2024SecretKey!";
                return new TeamLearningApiClient(TEST_API_URL, TEST_API_KEY);
            }
            // ═══════════════════════════════════════════════════════════════════
            
            // Use hardcoded AppConfig (no VS Options needed)
            if (AppConfig.EnableTeamLearning)
            {
                return new TeamLearningApiClient(AppConfig.ApiUrl, AppConfig.ApiKey);
            }
            return null;
        }

        /// <summary>
        /// Gets the contributor name from settings
        /// </summary>
        private string GetContributorName()
        {
            // TESTING MODE: Return a test contributor name
            const bool TESTING_MODE = true;
            if (TESTING_MODE)
            {
                return "Ankith"; // Your name for testing
            }
            
            // Use hardcoded AppConfig
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
                    btn.Opacity = 0.5;
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
