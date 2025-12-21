using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Model for AI configuration settings
    /// </summary>
    public class AiConfig
    {
        public string AzureOpenAIEndpoint { get; set; } = "";
        public string AzureOpenAIKey { get; set; } = "";
        public string DeploymentName { get; set; } = "gpt-35-turbo";
    }
}
