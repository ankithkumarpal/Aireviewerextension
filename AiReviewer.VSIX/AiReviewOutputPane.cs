using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using AiReviewer.Shared;

namespace AiReviewer.VSIX
{
    internal class AiReviewOutputPane
    {
        private static IVsOutputWindowPane _pane;
        private static readonly Guid _paneGuid = new Guid("B8F4E5D1-9A2C-4F3E-8D7C-1A2B3C4D5E6F");
        private static List<ReviewResult> _currentResults = new List<ReviewResult>();

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); 

            if (_pane != null)
                return;

            var outputWindow = serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return;

            // Try to get existing pane
            Guid paneGuid = _paneGuid;
            outputWindow.GetPane(ref paneGuid, out _pane);

            // Create if doesn't exist
            if (_pane == null)
            {
                outputWindow.CreatePane(ref paneGuid, "AI Code Reviewer", 1, 1);
                outputWindow.GetPane(ref paneGuid, out _pane);
            }
        }

        public static void ShowResults(List<ReviewResult> results, string repositoryPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_pane == null)
                return;

            // Store results for "Apply Fix" functionality
            _currentResults = results;

            _pane.Activate();
            _pane.Clear();

            // Header
            WriteLine("═══════════════════════════════════════════════════════════════════");
            WriteLine($"AI CODE REVIEW RESULTS - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLine($"Repository: {repositoryPath}");
            WriteLine("═══════════════════════════════════════════════════════════════════");
            WriteLine("");

            if (results.Count == 0)
            {
                WriteLine("✅ No issues found! Your staged changes look good.");
                WriteLine("");
                return;
            }

            // Summary
            var highCount = results.FindAll(r => r.Severity == "High").Count;
            var mediumCount = results.FindAll(r => r.Severity == "Medium").Count;
            var lowCount = results.FindAll(r => r.Severity == "Low").Count;

            WriteLine($"Found {results.Count} issue(s):");
            if (highCount > 0) WriteLine($"  🔴 High:   {highCount}");
            if (mediumCount > 0) WriteLine($"  🟡 Medium: {mediumCount}");
            if (lowCount > 0) WriteLine($"  🟢 Low:    {lowCount}");
            WriteLine("");
            WriteLine("───────────────────────────────────────────────────────────────────");
            WriteLine("");

            // Group by severity
            var groups = new[] { "High", "Medium", "Low" };
            foreach (var severity in groups)
            {
                var items = results.FindAll(r => r.Severity == severity);
                if (items.Count == 0)
                    continue;

                string icon = severity == "High" ? "🔴" : severity == "Medium" ? "🟡" : "🟢";
                WriteLine($"{icon} {severity.ToUpper()} SEVERITY ({items.Count} issue{(items.Count > 1 ? "s" : "")})");
                WriteLine("");

                for (int i = 0; i < items.Count; i++)
                {
                    var result = items[i];
                    var resultIndex = _currentResults.IndexOf(result) + 1;
                    
                    WriteLine($"  [{resultIndex}] {result.FilePath}:{result.LineNumber}");
                    WriteLine($"      Issue:      {result.Issue}");
                    
                    // Show before/after code comparison
                    if (!string.IsNullOrEmpty(result.CodeSnippet))
                    {
                        WriteLine($"      ❌ Your Code:  {result.CodeSnippet}");
                    }
                    
                    if (!string.IsNullOrEmpty(result.FixedCode))
                    {
                        WriteLine($"      ✅ Fixed Code: {result.FixedCode}");
                        WriteLine($"      🔧 To apply: Run command 'AI Reviewer: Apply Fix #{resultIndex}' from Command Palette");
                    }
                    
                    WriteLine($"      💡 Why:        {result.Suggestion}");
                    if (!string.IsNullOrEmpty(result.Rule))
                        WriteLine($"      📋 Rule:       {result.Rule}");
                    WriteLine("");
                }

                WriteLine("");
            }

            WriteLine("═══════════════════════════════════════════════════════════════════");
            WriteLine("💡 Click on file paths above to navigate to the issue location.");
            WriteLine("═══════════════════════════════════════════════════════════════════");
        }

        private static void WriteLine(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pane?.OutputStringThreadSafe(message + Environment.NewLine);
        }

        public static void Clear()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pane?.Clear();
        }

        public static ReviewResult GetResult(int index)
        {
            if (index < 1 || index > _currentResults.Count)
                return null;
            return _currentResults[index - 1];
        }

        public static int GetResultCount()
        {
            return _currentResults.Count;
        }
    }
}
