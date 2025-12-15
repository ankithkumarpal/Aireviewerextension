using AiReviewer.Shared.Models;
using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiReviewer.Shared.StaticHelper
{
    public static class AiConfigLoader
    {
        private static readonly string ConfigFileName = "ai-reviewer-config.yaml";

        public static AiConfig Load(string repoRoot)
        {
            // Look for config in .config/ai-reviewer/ or fallback to environment variables
            var configPath = Path.Combine(repoRoot, ".config", "ai-reviewer", ConfigFileName);

            if (File.Exists(configPath))
            {
                var yaml = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                return deserializer.Deserialize<AiConfig>(yaml);
            }

            // Fallback to environment variables
            return new AiConfig
            {
                AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "",
                AzureOpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "",
                DeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-35-turbo"
            };
        }

        public static void CreateSampleConfig(string repoRoot)
        {
            var configDir = Path.Combine(repoRoot, ".config", "ai-reviewer");
            Directory.CreateDirectory(configDir);

            var configPath = Path.Combine(configDir, ConfigFileName);
            var sampleConfig = @"# AI Reviewer Configuration
# Get your Azure OpenAI credentials from: https://portal.azure.com

azureOpenAIEndpoint: https://your-resource.openai.azure.com/
azureOpenAIKey: your-api-key-here
deploymentName: gpt-35-turbo  # or gpt-4
";

            File.WriteAllText(configPath, sampleConfig);
        }
    }
}
