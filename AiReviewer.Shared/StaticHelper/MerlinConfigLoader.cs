using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiReviewer.Shared.StaticHelper
{
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
                // but can add guards or defaults here.
                if (c.Id == null)
                {
                    c.Id = System.Guid.NewGuid().ToString("N");
                }
            }

            return cfg;
        }
    }
}
