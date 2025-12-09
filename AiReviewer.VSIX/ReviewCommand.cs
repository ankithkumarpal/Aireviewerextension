
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using AiReviewer.Shared;
using EnvDTE;
using EnvDTE80;

namespace AiReviewer.VSIX
{
    internal static class ReviewCommand
    {
        public static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () => await RunAsync());
        }

        public static async Task RunAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string workingDir = null;

            // Try to get the current working directory from VS
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
            
            // Try solution directory first
            if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
            {
                workingDir = Path.GetDirectoryName(dte.Solution.FullName);
            }
            else if (dte?.ActiveDocument?.Path != null)
            {
                // Use active document's path if in folder mode
                workingDir = dte.ActiveDocument.Path;
            }
            else
            {
                // Last resort: check if there's any open document
                if (dte?.Documents != null && dte.Documents.Count > 0)
                {
                    try
                    {
                        workingDir = dte.Documents.Item(1).Path;
                    }
                    catch { }
                }
                
                // If still no working dir, try to get it from solution (even if it's empty in folder mode)
                if (string.IsNullOrEmpty(workingDir) && dte?.Solution?.FullName != null)
                {
                    // In folder mode, Solution.FullName might be the folder path
                    var solPath = dte.Solution.FullName;
                    if (Directory.Exists(solPath))
                    {
                        workingDir = solPath;
                    }
                    else if (!string.IsNullOrEmpty(solPath))
                    {
                        workingDir = Path.GetDirectoryName(solPath);
                    }
                }
            }

            if (string.IsNullOrEmpty(workingDir))
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    "Could not determine workspace folder. Please open a file from your git repository, or open a solution.",
                    "AI Reviewer",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var repo = GitDiff.FindRepoRoot(workingDir);
            if (string.IsNullOrEmpty(repo))
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    $"No git repo found starting from:\n{workingDir}",
                    "AI Reviewer",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var cfgPath = Path.Combine(repo, ".config", "merlinBot", "PullRequestAssistant.yaml");
            if (!File.Exists(cfgPath))
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    $"Config not found:\n{cfgPath}\n\nRepo: {repo}\nWorking: {workingDir}",
                    "AI Reviewer",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var cfg = MerlinConfigLoader.Load(cfgPath);
            var diff = GitDiff.GetStagedUnifiedDiff(repo);
            var patches = GitDiff.ParseUnified(diff);

            if (patches.Count == 0)
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    "No staged changes found. Please stage your changes with 'git add' first.",
                    "AI Reviewer",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            StagedServices.Initialize(patches);

            // Load AI configuration
            var aiConfig = AiConfigLoader.Load(repo);
            
            if (string.IsNullOrEmpty(aiConfig.AzureOpenAIEndpoint) || string.IsNullOrEmpty(aiConfig.AzureOpenAIKey))
            {
                var result = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    "Azure OpenAI is not configured.\n\nWould you like to create a sample configuration file?",
                    "AI Reviewer - Configuration Required",
                    OLEMSGICON.OLEMSGICON_QUESTION,
                    OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                
                if (result == 6) // Yes
                {
                    AiConfigLoader.CreateSampleConfig(repo);
                    var configPath = Path.Combine(repo, ".config", "ai-reviewer", "ai-reviewer-config.yaml");
                    VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                        $"Sample configuration created at:\n{configPath}\n\nPlease update it with your Azure OpenAI credentials.",
                        "AI Reviewer",
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
                return;
            }

            // Show progress
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                $"Analyzing {patches.Count} file(s) with AI...\n\nThis may take a few seconds.",
                "AI Reviewer",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            try
            {
                // Call AI review service
                var aiService = new AiReviewService(aiConfig.AzureOpenAIEndpoint, aiConfig.AzureOpenAIKey, aiConfig.DeploymentName);
                var results = await aiService.ReviewCodeAsync(patches, cfg);

                // Display results
                if (results.Count == 0)
                {
                    VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                        "✅ No issues found!\n\nYour code looks good.",
                        "AI Reviewer",
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
                else
                {
                    var summary = $"Found {results.Count} issue(s):\n\n";
                    var highCount = results.Count(r => r.Severity == "High");
                    var mediumCount = results.Count(r => r.Severity == "Medium");
                    var lowCount = results.Count(r => r.Severity == "Low");
                    
                    if (highCount > 0) summary += $"🔴 High: {highCount}\n";
                    if (mediumCount > 0) summary += $"🟡 Medium: {mediumCount}\n";
                    if (lowCount > 0) summary += $"🟢 Low: {lowCount}\n";
                    
                    summary += "\nCheck the Error List for details.";
                    
                    VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                        summary,
                        "AI Reviewer",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    
                    // TODO: Display results in Error List window
                    // We'll implement this in the next step
                }
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                    $"Error during AI review:\n{ex.Message}",
                    "AI Reviewer - Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}