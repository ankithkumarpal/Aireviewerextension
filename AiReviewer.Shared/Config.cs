// AiReviewer.Shared/Config.cs
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiReviewer.Shared
{
    public enum Severity { Info, Warning, Error }

    public sealed class Pattern
    {
        public string Type { get; set; }      // "metrics" | "roslyn-inspect" | "regex" | "list"
        public object Value { get; set; }     // shape depends on Type
    }

    public sealed class Check
    {
        public string Id { get; set; } = "";
        public List<string> AppliesTo { get; set; } = new List<string>();
        public Severity Severity { get; set; } = Severity.Warning;
        public string Description { get; set; } = "";
        public Pattern Pattern { get; set; }
        public string Guidance { get; set; } = "";
    }

    public sealed class MerlinConfig
    {
        public int Version { get; set; } = 1;
        public List<string> IncludePaths { get; set; } = new List<string>();
        public List<string> ExcludePaths { get; set; } = new List<string>();
        public List<Check> Checks { get; set; } = new List<Check>();
    }

    public static class MerlinConfigLoader
    {
        public static MerlinConfig Load(string path)
        {
            var yaml = System.IO.File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var cfg = deserializer.Deserialize<MerlinConfig>(yaml);

            // Normalize severities if YAML uses strings
            foreach (var c in cfg.Checks)
            {
                // If Severity was serialized as string, YamlDotNet will map it automatically
                // but you can add guards or defaults here.
                if (c.Id == null)
                {
                    c.Id = System.Guid.NewGuid().ToString("N");
                }
            }

            return cfg;
        }
    }
}
