using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace AiReviewer.VSIX
{
    [Guid("1B2E3F4A-5C6D-7E8F-9A0B-1C2D3E4F5A6B")]
    public class AiReviewerToolWindow : ToolWindowPane
    {
        public AiReviewerToolWindow() : base(null)
        {
            Caption = "AI Code Reviewer";
            
            // Set dark background immediately to prevent white flash
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), // #1E1E1E - VS dark theme
                Child = new AiReviewerToolWindowControl()
            };
            Content = container;
        }
    }
}
