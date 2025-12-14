using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AiReviewer.VSIX
{
    /// <summary>
    /// Handles the "Review Staged Changes" menu command from Tools menu
    /// </summary>
    internal static class ReviewCommand
    {
        /// <summary>
        /// Called from menu command - opens tool window and starts review
        /// </summary>
        public static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                System.Diagnostics.Debug.WriteLine("[ReviewCommand] Execute called from menu");
                
                // Open the tool window
                var package = AiReviewerPackage.Instance;
                if (package == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ReviewCommand] Package instance is null");
                    return;
                }

                var window = package.FindToolWindow(typeof(AiReviewerToolWindow), 0, true);
                if (window?.Frame == null)
                {
                    VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,
                        "Could not create AI Reviewer tool window.",
                        "AI Reviewer",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                // Show the tool window
                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
                
                System.Diagnostics.Debug.WriteLine("[ReviewCommand] Tool window shown");

                // Get the control and trigger review
                var control = GetControlFromWindow(window);
                if (control != null)
                {
                    System.Diagnostics.Debug.WriteLine("[ReviewCommand] Triggering review...");
                    // Trigger the review button click programmatically
                    control.TriggerReview();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ReviewCommand] Could not get control from window");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReviewCommand] Error: {ex.Message}");
            }
        }

        private static AiReviewerToolWindowControl GetControlFromWindow(ToolWindowPane window)
        {
            // The window.Content might be a Border wrapping the control
            if (window.Content is System.Windows.Controls.Border border)
            {
                return border.Child as AiReviewerToolWindowControl;
            }
            return window.Content as AiReviewerToolWindowControl;
        }
    }
}
