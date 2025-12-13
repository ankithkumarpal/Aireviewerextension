using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace AiReviewer.VSIX
{
    [Guid("1B2E3F4A-5C6D-7E8F-9A0B-1C2D3E4F5A6B")]
    public class AiReviewerToolWindow : ToolWindowPane
    {
        public AiReviewerToolWindow() : base(null)
        {
            Caption = "AI Code Reviewer";
            Content = new AiReviewerToolWindowControl();
        }
    }
}
