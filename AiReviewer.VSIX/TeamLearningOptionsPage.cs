using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AiReviewer.VSIX
{
    /// <summary>
    /// Options page for AI Reviewer Team Learning settings.
    /// 
    /// This appears in Visual Studio under:
    ///   Tools > Options > AI Reviewer > Team Learning
    /// 
    /// Settings are stored in VS's user settings (AppData), NOT in any repository.
    /// </summary>
    [ComVisible(true)]
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public class TeamLearningOptionsPage : DialogPage
    {
        // ─────────────────────────────────────────────────────────────────────────
        // Team Learning Settings
        // ─────────────────────────────────────────────────────────────────────────

        private bool _enableTeamLearning = false;
        private string _apiUrl = "";
        private string _apiKey = "";
        private string _contributorName = "";

        /// <summary>
        /// Enable or disable team learning feature.
        /// When enabled, feedback is sent to shared Azure storage.
        /// When disabled, learning is stored locally only.
        /// </summary>
        [Category("Team Learning")]
        [DisplayName("Enable Team Learning")]
        [Description("Share learning patterns with your team via Azure. When disabled, patterns are stored locally only.")]
        [DefaultValue(false)]
        public bool EnableTeamLearning
        {
            get => _enableTeamLearning;
            set => _enableTeamLearning = value;
        }

        /// <summary>
        /// The URL of your Team Learning API (Azure Function).
        /// Example: https://ai-reviewer-api.azurewebsites.net/api
        /// </summary>
        [Category("Team Learning")]
        [DisplayName("API URL")]
        [Description("URL of your Team Learning API (Azure Function). Example: https://your-function-app.azurewebsites.net/api")]
        [DefaultValue("")]
        public string ApiUrl
        {
            get => _apiUrl;
            set => _apiUrl = value?.Trim() ?? "";
        }

        /// <summary>
        /// The API key for authentication.
        /// This is validated by the Azure Function on each request.
        /// </summary>
        [Category("Team Learning")]
        [DisplayName("API Key")]
        [Description("API key for authentication. Get this from your Azure Function's application settings.")]
        [PasswordPropertyText(true)]
        [DefaultValue("")]
        public string ApiKey
        {
            get => _apiKey;
            set => _apiKey = value ?? "";
        }

        /// <summary>
        /// Your name or alias to identify your feedback contributions.
        /// Shows up in team statistics and contributor leaderboards.
        /// </summary>
        [Category("Team Learning")]
        [DisplayName("Contributor Name")]
        [Description("Your name or alias to identify your feedback. Shows up in team statistics.")]
        public string ContributorName
        {
            get => string.IsNullOrEmpty(_contributorName) ? Environment.UserName : _contributorName;
            set => _contributorName = value ?? "";
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Validation
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that all required settings are configured when Team Learning is enabled.
        /// </summary>
        public bool IsConfigurationValid
        {
            get
            {
                if (!EnableTeamLearning)
                    return true; // Disabled = always valid

                return !string.IsNullOrWhiteSpace(ApiUrl) && 
                       !string.IsNullOrWhiteSpace(ApiKey);
            }
        }

        /// <summary>
        /// Gets a user-friendly message about configuration status.
        /// </summary>
        public string ConfigurationStatus
        {
            get
            {
                if (!EnableTeamLearning)
                    return "Team Learning is disabled. Patterns are stored locally.";

                if (string.IsNullOrWhiteSpace(ApiUrl))
                    return "⚠️ API URL is not configured";

                if (string.IsNullOrWhiteSpace(ApiKey))
                    return "⚠️ API Key is not configured";

                return $"✅ Team Learning enabled as {ContributorName}";
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helper Methods
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets the full URL for an API endpoint.
        /// </summary>
        public string GetEndpointUrl(string endpoint)
        {
            var baseUrl = ApiUrl?.TrimEnd('/') ?? "";
            return $"{baseUrl}/{endpoint}";
        }
    }
}
