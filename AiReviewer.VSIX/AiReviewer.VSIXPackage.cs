
using AiReviewer.VSIX;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AiReviewer.VSIX
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("AI Reviewer", "Review staged changes before PR", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(AiReviewerToolWindow))]
    [Guid(PackageGuidString)]
    public sealed class AiReviewerPackage : AsyncPackage
    {
        public const string PackageGuidString = "BA5749DA-8661-4E2F-9803-BA0FC420ACD6";
        public static AiReviewerPackage Instance { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            OleMenuCommandService commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var cmdSetGuid = new Guid("D27F4BBD-41B9-4B8E-9B40-2D4A48A2B9C7");
                
                // Register Show Tool Window command
                var showToolWindowCommandID = new CommandID(cmdSetGuid, 0x0100);
                var showToolWindowMenuItem = new MenuCommand(ShowToolWindowCommand, showToolWindowCommandID);
                commandService.AddCommand(showToolWindowMenuItem);
                
                // Register Review Staged Changes command
                var reviewCommandID = new CommandID(cmdSetGuid, 0x0101);
                var reviewMenuItem = new MenuCommand(ReviewCommand.Execute, reviewCommandID);
                commandService.AddCommand(reviewMenuItem);
            }
        }

        private void ShowToolWindowCommand(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            var window = FindToolWindow(typeof(AiReviewerToolWindow), 0, true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            var windowFrame = (Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}
