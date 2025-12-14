namespace AiReviewer.VSIX
{
    /// <summary>
    /// Hardcoded configuration for NNF Hackathon.
    /// No local settings needed - just install and use!
    /// </summary>
    internal static class AppConfig
    {
        // ═══════════════════════════════════════════════════════════════════════════════
        // HARDCODED VALUES FOR NNF TEAM
        // ═══════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Azure Function API URL
        /// </summary>
        public const string ApiUrl = "https://ai-reviewer-teamlearning-apc4dvfhgxaze3h9.eastus-01.azurewebsites.net/api";
        
        /// <summary>
        /// API Key for authentication
        /// </summary>
        public const string ApiKey = "TeamLearning2024SecretKey!";
        
        /// <summary>
        /// Team Learning is always enabled
        /// </summary>
        public const bool EnableTeamLearning = true;
        
        /// <summary>
        /// Gets contributor name from Windows username
        /// </summary>
        public static string ContributorName => System.Environment.UserName;
    }
}
