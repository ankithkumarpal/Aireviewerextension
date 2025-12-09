
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
    [Guid(PackageGuidString)]
    public sealed class AiReviewerPackage : AsyncPackage
    {
        public const string PackageGuidString = "BA5749DA-8661-4E2F-9803-BA0FC420ACD6";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            OleMenuCommandService commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(new Guid("D27F4BBD-41B9-4B8E-9B40-2D4A48A2B9C7"), 0x0101);
                var menuItem = new MenuCommand(ReviewCommand.Execute, menuCommandID);
                commandService.AddCommand(menuItem);
            }
        }
    }
}
