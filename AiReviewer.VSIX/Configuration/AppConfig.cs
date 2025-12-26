namespace AiReviewer.VSIX.Configuration
{
    /// <summary>
    /// Configuration for AI Code Reviewer.
    /// Uses Azure AD authentication - secure and no API keys in code!
    /// </summary>
    internal static class AppConfig
    {
        /// <summary>
        /// Review Service API URL (handles AI code reviews)
        /// For local testing: http://localhost:5121/api
        /// For production: Deploy AiReviewer.ReviewService to Azure App Service
        /// </summary>
        public const string ReviewServiceUrl = "http://localhost:5121/api";
        
        /// <summary>
        /// Azure Functions API URL (handles standards, feedback, patterns)
        /// </summary>
        public const string ApiUrl = "https://ai-reviewer-teamlearning-apc4dvfhgxaze3h9.eastus-01.azurewebsites.net/api";

        // AZURE AD CONFIGURATION

        /// <summary>
        /// Azure AD Client ID (Application ID from App Registration)
        /// </summary>
        public const string AzureAdClientId = "186b2d8b-fff5-4211-9191-69f257628caa";

        /// <summary>
        /// Azure AD Tenant ID - Single tenant (your directory only)
        /// External users must be invited as B2B guests to authenticate
        /// </summary>
        public const string AzureAdTenantId = "03a7f622-85c1-4167-895f-808fb8fc249a";

        /// <summary>
        /// The scope to request when acquiring token.
        /// Must match the scope defined in "Expose an API" in App Registration
        /// </summary>
        public static string AzureAdScope => $"api://{AzureAdClientId}/access_as_user";

        /// <summary>
        /// Authority URL for Azure AD authentication
        /// </summary>
        public static string AzureAdAuthority => $"https://login.microsoftonline.com/{AzureAdTenantId}";

      // APPLICATION SETTINGS

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
