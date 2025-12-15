using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AiReviewer.Functions.Models
{

    /// <summary>
    /// Response model for /api/config endpoint
    /// </summary>
    public class AiConfigResponse
    {
        public string AzureOpenAIEndpoint { get; set; } = string.Empty;
        public string AzureOpenAIKey { get; set; } = string.Empty;
        public string DeploymentName { get; set; } = "gpt-4o-mini";
    }
}
