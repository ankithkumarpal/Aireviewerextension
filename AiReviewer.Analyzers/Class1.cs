// ============================================================================
// AI Reviewer - Roslyn Analyzers Project
// ============================================================================
// 
// Purpose: This project is intended to contain Roslyn-based code analyzers
// that can run directly in Visual Studio to provide real-time code analysis.
// 
// Current Status: NOT IMPLEMENTED - Placeholder project
// 
// Future Implementation Ideas:
// - Custom Roslyn analyzers for NNF coding standards
// - Real-time code smell detection
// - Security vulnerability detection
// - Code quality metrics calculation
// 
// To implement Roslyn analyzers:
// 1. Add DiagnosticAnalyzer classes
// 2. Add CodeFixProvider classes
// 3. Configure analyzer registration in the VSIX project
// 
// See: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix
// ============================================================================

namespace AiReviewer.Analyzers
{
    /// <summary>
    /// Placeholder class - implement Roslyn analyzers here.
    /// </summary>
    public static class AnalyzerConstants
    {
        /// <summary>
        /// Category for AI Reviewer diagnostics.
        /// </summary>
        public const string DiagnosticCategory = "AiReviewer";
        
        /// <summary>
        /// Prefix for all AI Reviewer diagnostic IDs.
        /// </summary>
        public const string DiagnosticIdPrefix = "AIR";
    }
}
