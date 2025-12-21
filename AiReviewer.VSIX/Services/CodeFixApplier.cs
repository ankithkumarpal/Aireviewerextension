using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using AiReviewer.Shared.Services;
using AiReviewer.Shared.Models;

namespace AiReviewer.VSIX.Services
{
    internal static class CodeFixApplier
    {
        public static void ApplyFix(ReviewResult result)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(result.FixedCode))
            {
                System.Windows.MessageBox.Show(
                    "No fix available for this issue.",
                    "AI Code Reviewer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Get DTE to access open documents
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte == null)
                    return;

                // Try to find and open the document
                var filePath = FindFullPath(result.FilePath, result.RepositoryPath, dte);
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    System.Windows.MessageBox.Show(
                        $"Could not find file: {result.FilePath}\n\nRepository: {result.RepositoryPath}",
                        "AI Code Reviewer",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Open the document
                var window = dte.ItemOperations.OpenFile(filePath);
                var textDoc = window.Document.Object("TextDocument") as TextDocument;
                if (textDoc == null)
                    return;

                // Navigate to the line
                var editPoint = textDoc.StartPoint.CreateEditPoint();
                editPoint.MoveToLineAndOffset(result.LineNumber, 1);

                // Get the current line text
                var lineEndPoint = editPoint.CreateEditPoint();
                lineEndPoint.EndOfLine();
                var currentLine = editPoint.GetText(lineEndPoint);

                // Check if the line matches what we expect (if we have the original snippet)
                if (!string.IsNullOrEmpty(result.CodeSnippet))
                {
                    var trimmedCurrent = currentLine.Trim();
                    var trimmedExpected = result.CodeSnippet.Trim();
                    
                    if (!trimmedCurrent.Contains(trimmedExpected))
                    {
                        var response = System.Windows.MessageBox.Show(
                            $"The code at line {result.LineNumber} has changed:\n\n" +
                            $"Expected: {result.CodeSnippet}\n" +
                            $"Found:    {currentLine.Trim()}\n\n" +
                            "Apply fix anyway?",
                            "AI Code Reviewer - Confirmation",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning,
                            System.Windows.MessageBoxResult.No);
                        
                        if (response == System.Windows.MessageBoxResult.No)
                            return;
                    }
                }

                // Get the indentation from the current line
                var indent = currentLine.Substring(0, currentLine.Length - currentLine.TrimStart().Length);
                var fixedCodeWithIndent = indent + result.FixedCode.Trim();

                // Replace the line
                editPoint.Delete(lineEndPoint);
                editPoint.Insert(fixedCodeWithIndent);

                // Show success message
                System.Windows.MessageBox.Show(
                    $"✅ Fix applied successfully!\n\nLine {result.LineNumber} updated.",
                    "AI Code Reviewer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error applying fix:\n{ex.Message}",
                    "AI Code Reviewer - Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private static string FindFullPath(string relativePath, string repositoryPath, DTE dte)
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

            // Try workspace folders
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
    }
}
