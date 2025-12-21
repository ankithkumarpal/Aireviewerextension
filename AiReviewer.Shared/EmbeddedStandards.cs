using AiReviewer.Shared.Models;
using System.Collections.Generic;

namespace AiReviewer.Shared
{
    /// <summary>
    /// Embedded default standards - used as fallback when API is unavailable
    /// These are the baseline NNF coding standards
    /// </summary>
    public static class EmbeddedStandards
    {
        public static StagebotConfig GetDefaults()
        {
            return new StagebotConfig
            {
                Version = 1,
                IncludePaths = new List<string> { "**/*.cs", "**/*.ts", "**/*.js" },
                ExcludePaths = new List<string> { "**/bin/**", "**/obj/**", "**/node_modules/**" },
                Checks = GetDefaultChecks()
            };
        }

        private static List<Check> GetDefaultChecks()
        {
            return new List<Check>
            {
                // Security
                new Check
                {
                    Id = "nnf-sec-001",
                    AppliesTo = new List<string> { ".cs", ".ts", ".js" },
                    Severity = Severity.Error,
                    Description = "Hardcoded secrets (API keys, passwords, connection strings)",
                    Guidance = "Use Azure Key Vault, environment variables, or secure configuration. Never commit secrets.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"(?i)(password|secret|apikey|api_key|connectionstring)\s*[=:]\s*[""'][^""']{8,}[""']"
                    }
                },
                new Check
                {
                    Id = "nnf-sec-002",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Error,
                    Description = "SQL injection risk - string concatenation in SQL",
                    Guidance = "Use parameterized queries or stored procedures. Never concatenate user input into SQL.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"(SqlCommand|ExecuteSql|FromSqlRaw).*\+.*"
                    }
                },

                // Async/Await
                new Check
                {
                    Id = "nnf-async-001",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "Blocking calls in async code (.Result, .Wait(), .GetAwaiter().GetResult())",
                    Guidance = "Use await instead of blocking. Blocking can cause deadlocks and thread pool starvation.",
                    Pattern = new Pattern
                    {
                        Type = "list",
                        Value = new List<string> { ".Result", ".Wait(", ".GetAwaiter().GetResult()" }
                    }
                },
                new Check
                {
                    Id = "nnf-async-002",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "Thread.Sleep in async code",
                    Guidance = "Use await Task.Delay() instead of Thread.Sleep() in async methods.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"Thread\.Sleep\s*\("
                    }
                },

                // Error Handling
                new Check
                {
                    Id = "nnf-err-001",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "Empty catch block - exceptions should be handled or logged",
                    Guidance = "Log exceptions or handle them appropriately. Empty catch blocks hide errors.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"catch\s*\([^)]*\)\s*\{\s*\}"
                    }
                },
                new Check
                {
                    Id = "nnf-err-002",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Info,
                    Description = "Catching generic Exception - prefer specific exception types",
                    Guidance = "Catch specific exceptions when possible. Generic catch may hide unexpected errors.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"catch\s*\(\s*Exception\s+\w+\s*\)"
                    }
                },

                // Resource Management
                new Check
                {
                    Id = "nnf-res-001",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "IDisposable created without using statement",
                    Guidance = "Wrap IDisposable objects in using statements or implement proper disposal.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"=\s*new\s+(FileStream|StreamReader|StreamWriter|SqlConnection|HttpClient)\s*\("
                    }
                },

                // Logging
                new Check
                {
                    Id = "nnf-log-001",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Info,
                    Description = "Console.WriteLine in production code",
                    Guidance = "Use ILogger for structured logging. Console output is not captured in production.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"Console\.(WriteLine|Write)\s*\("
                    }
                },
                new Check
                {
                    Id = "nnf-log-002",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Info,
                    Description = "Debug.WriteLine in production code",
                    Guidance = "Remove debug statements or use ILogger with appropriate log levels.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"Debug\.(WriteLine|Print)\s*\("
                    }
                },

                // Code Quality
                new Check
                {
                    Id = "nnf-qual-001",
                    AppliesTo = new List<string> { ".cs", ".ts", ".js" },
                    Severity = Severity.Info,
                    Description = "TODO/FIXME/HACK comments should be addressed",
                    Guidance = "Create work items for TODOs and address them before merging to main branches.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"//\s*(TODO|FIXME|HACK|XXX)[\s:]"
                    }
                },
                new Check
                {
                    Id = "nnf-qual-002",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "Magic numbers - use named constants",
                    Guidance = "Extract magic numbers into well-named constants for clarity and maintainability.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"[^0-9a-zA-Z_]([2-9]\d{2,}|[1-9]\d{3,})[^0-9a-zA-Z_]"
                    }
                },

                // Configuration
                new Check
                {
                    Id = "nnf-cfg-001",
                    AppliesTo = new List<string> { ".cs" },
                    Severity = Severity.Warning,
                    Description = "Hardcoded URLs or endpoints",
                    Guidance = "Use configuration (appsettings, environment variables) for URLs and endpoints.",
                    Pattern = new Pattern
                    {
                        Type = "regex",
                        Value = @"""https?://[^""]+"""
                    }
                }
            };
        }
    }
}
