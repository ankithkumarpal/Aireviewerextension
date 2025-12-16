using System;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// API response model for NNF standards
    /// </summary>
    public class NnfStandardsResponse
    {
        public string YamlContent { get; set; } = "";
        public int Version { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public StagebotConfig ParsedConfig { get; set; }
    }

    /// <summary>
    /// API request model for updating NNF standards
    /// </summary>
    public class UpdateNnfStandardsRequest
    {
        public string YamlContent { get; set; } = "";
        public string UpdatedBy { get; set; } = "";
        public string ChangeDescription { get; set; } = "";
    }
}
