using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AiReviewer.Shared.StaticHelper
{
    public static class StagebotConfigLoader
    {
        /// <summary>
        /// Standard config paths in priority order (first found wins for base, then merges)
        /// </summary>
        private static readonly string[] ConfigPaths = new[]
        {
            ".config/stagebot/PullRequestAssistant.yaml",
            ".config/stagebot/stagebot.yaml",
            ".stagebot.yaml",
            "stagebot.yaml"
        };

        /// <summary>
        /// Central standards URL or local path (can be overridden via env var)
        /// </summary>
        public static string CentralStandardsPath { get; set; } = "";

        /// <summary>
        /// Load config from a specific path
        /// </summary>
        public static StagebotConfig Load(string path)
        {
            var yaml = File.ReadAllText(path);
            return ParseYaml(yaml);
        }

        /// <summary>
        /// Load config with automatic discovery and central standards merging
        /// </summary>
        public static StagebotConfig LoadWithFallback(string repositoryRoot, string centralStandardsPath = null)
        {
            StagebotConfig localConfig = null;
            StagebotConfig centralConfig = null;

            // 1. Try to load local config from standard paths
            foreach (var relativePath in ConfigPaths)
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath);
                if (File.Exists(fullPath))
                {
                    localConfig = Load(fullPath);
                    System.Diagnostics.Debug.WriteLine($"[Stagebot] Loaded local config from: {fullPath}");
                    break;
                }
            }

            // 2. Load central standards if available and inheritance is enabled
            var centralPath = centralStandardsPath ?? CentralStandardsPath ?? Environment.GetEnvironmentVariable("STAGEBOT_CENTRAL_STANDARDS");
            if (!string.IsNullOrEmpty(centralPath) && File.Exists(centralPath))
            {
                try
                {
                    centralConfig = Load(centralPath);
                    System.Diagnostics.Debug.WriteLine($"[Stagebot] Loaded central standards from: {centralPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Stagebot] Failed to load central standards: {ex.Message}");
                }
            }

            // 3. Merge configs (local overrides central)
            if (localConfig == null && centralConfig == null)
            {
                System.Diagnostics.Debug.WriteLine("[Stagebot] No config found, using defaults");
                return new StagebotConfig();
            }

            if (localConfig == null)
            {
                return centralConfig;
            }

            if (centralConfig == null || !localConfig.InheritCentralStandards)
            {
                return localConfig;
            }

            // Merge: central as base, local overrides
            return MergeConfigs(centralConfig, localConfig);
        }

        /// <summary>
        /// Load config from a repository (checks standard paths)
        /// Returns null if no config found
        /// </summary>
        public static StagebotConfig LoadFromRepository(string repositoryRoot)
        {
            if (string.IsNullOrEmpty(repositoryRoot))
                return null;

            foreach (var relativePath in ConfigPaths)
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var config = Load(fullPath);
                        System.Diagnostics.Debug.WriteLine($"[Stagebot] Loaded repo config: {fullPath}");
                        return config;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Stagebot] Error loading {fullPath}: {ex.Message}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Stagebot] No config found in repo: {repositoryRoot}");
            return null;
        }

        /// <summary>
        /// Merge two configs: base + override. Override wins for duplicates.
        /// </summary>
        public static StagebotConfig MergeConfigs(StagebotConfig baseConfig, StagebotConfig overrideConfig)
        {
            var merged = new StagebotConfig
            {
                Version = Math.Max(baseConfig.Version, overrideConfig.Version),
                InheritCentralStandards = overrideConfig.InheritCentralStandards,
                IncludePaths = overrideConfig.IncludePaths.Count > 0 
                    ? overrideConfig.IncludePaths 
                    : baseConfig.IncludePaths,
                ExcludePaths = baseConfig.ExcludePaths
                    .Concat(overrideConfig.ExcludePaths)
                    .Distinct()
                    .ToList()
            };

            // Merge checks: override replaces by Id, others are added
            var checkDict = new Dictionary<string, Check>(StringComparer.OrdinalIgnoreCase);
            foreach (var check in baseConfig.Checks)
            {
                checkDict[check.Id] = check;
            }
            foreach (var check in overrideConfig.Checks)
            {
                checkDict[check.Id] = check; // Override
            }
            merged.Checks = checkDict.Values.ToList();

            // Merge PR checks the same way
            var prCheckDict = new Dictionary<string, PrCheck>(StringComparer.OrdinalIgnoreCase);
            foreach (var check in baseConfig.PrChecks ?? new List<PrCheck>())
            {
                prCheckDict[check.Id] = check;
            }
            foreach (var check in overrideConfig.PrChecks ?? new List<PrCheck>())
            {
                prCheckDict[check.Id] = check;
            }
            merged.PrChecks = prCheckDict.Values.ToList();

            return merged;
        }

        private static StagebotConfig ParseYaml(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var cfg = deserializer.Deserialize<StagebotConfig>(yaml) ?? new StagebotConfig();

            // Normalize: assign IDs if missing
            foreach (var c in cfg.Checks ?? new List<Check>())
            {
                if (string.IsNullOrEmpty(c.Id))
                {
                    c.Id = Guid.NewGuid().ToString("N");
                }
            }

            foreach (var c in cfg.PrChecks ?? new List<PrCheck>())
            {
                if (string.IsNullOrEmpty(c.Id))
                {
                    c.Id = Guid.NewGuid().ToString("N");
                }
            }

            return cfg;
        }
    }
}
