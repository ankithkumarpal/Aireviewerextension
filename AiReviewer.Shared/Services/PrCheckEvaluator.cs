using AiReviewer.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AiReviewer.Shared.Services
{
    /// <summary>
    /// Evaluates PR-level checks against PR metadata
    /// </summary>
    public class PrCheckEvaluator
    {
        /// <summary>
        /// Evaluate all PR checks against the given metadata
        /// </summary>
        public List<PrCheckResult> EvaluatePrChecks(StagebotConfig config, PrMetadata pr)
        {
            var results = new List<PrCheckResult>();

            if (config?.PrChecks == null || pr == null)
                return results;

            foreach (var check in config.PrChecks)
            {
                var result = EvaluateCheck(check, pr);
                results.Add(result);
            }

            return results;
        }

        private PrCheckResult EvaluateCheck(PrCheck check, PrMetadata pr)
        {
            var result = new PrCheckResult
            {
                CheckId = check.Id,
                Severity = check.Severity,
                Guidance = check.Guidance,
                Passed = true,
                Message = ""
            };

            try
            {
                switch (check.Type?.ToLowerInvariant())
                {
                    case "title_pattern":
                        result = EvaluateTitlePattern(check, pr);
                        break;

                    case "description_required":
                        result = EvaluateDescriptionRequired(check, pr);
                        break;

                    case "description_min_length":
                        result = EvaluateDescriptionMinLength(check, pr);
                        break;

                    case "max_files":
                        result = EvaluateMaxFiles(check, pr);
                        break;

                    case "max_lines":
                        result = EvaluateMaxLines(check, pr);
                        break;

                    case "required_labels":
                        result = EvaluateRequiredLabels(check, pr);
                        break;

                    case "forbidden_labels":
                        result = EvaluateForbiddenLabels(check, pr);
                        break;

                    case "branch_pattern":
                        result = EvaluateBranchPattern(check, pr);
                        break;

                    default:
                        result.Message = $"Unknown check type: {check.Type}";
                        result.Passed = true; // Don't fail on unknown types
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Message = $"Check evaluation failed: {ex.Message}";
            }

            result.CheckId = check.Id;
            result.Severity = check.Severity;
            result.Guidance = check.Guidance;

            return result;
        }

        private PrCheckResult EvaluateTitlePattern(PrCheck check, PrMetadata pr)
        {
            var pattern = check.Value?.ToString() ?? "";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var passed = regex.IsMatch(pr.Title ?? "");

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? $"PR title matches required pattern" 
                    : $"PR title '{pr.Title}' does not match pattern '{pattern}'. {check.Description}"
            };
        }

        private PrCheckResult EvaluateDescriptionRequired(PrCheck check, PrMetadata pr)
        {
            var passed = !string.IsNullOrWhiteSpace(pr.Description);

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? "PR has a description" 
                    : "PR description is required. Please add a meaningful description explaining the changes."
            };
        }

        private PrCheckResult EvaluateDescriptionMinLength(PrCheck check, PrMetadata pr)
        {
            var minLength = Convert.ToInt32(check.Value ?? 50);
            var actualLength = (pr.Description ?? "").Trim().Length;
            var passed = actualLength >= minLength;

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? $"PR description meets minimum length ({actualLength} chars)" 
                    : $"PR description is too short ({actualLength} chars). Minimum required: {minLength} chars."
            };
        }

        private PrCheckResult EvaluateMaxFiles(PrCheck check, PrMetadata pr)
        {
            var maxFiles = Convert.ToInt32(check.Value ?? 20);
            var fileCount = pr.FilesChanged?.Count ?? 0;
            var passed = fileCount <= maxFiles;

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? $"PR has {fileCount} files (limit: {maxFiles})" 
                    : $"PR has too many files ({fileCount}). Maximum allowed: {maxFiles}. Consider splitting into smaller PRs."
            };
        }

        private PrCheckResult EvaluateMaxLines(PrCheck check, PrMetadata pr)
        {
            var maxLines = Convert.ToInt32(check.Value ?? 500);
            var totalLines = pr.TotalAdditions + pr.TotalDeletions;
            var passed = totalLines <= maxLines;

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? $"PR has {totalLines} changed lines (limit: {maxLines})" 
                    : $"PR is too large ({totalLines} lines changed). Maximum: {maxLines}. Consider splitting into smaller PRs."
            };
        }

        private PrCheckResult EvaluateRequiredLabels(PrCheck check, PrMetadata pr)
        {
            var requiredLabels = ParseStringList(check.Value);
            var prLabels = pr.Labels ?? new List<string>();
            var hasRequired = requiredLabels.Any(req => 
                prLabels.Any(l => l.Equals(req, StringComparison.OrdinalIgnoreCase)));

            return new PrCheckResult
            {
                Passed = hasRequired,
                Message = hasRequired 
                    ? "PR has required labels" 
                    : $"PR must have at least one of these labels: {string.Join(", ", requiredLabels)}"
            };
        }

        private PrCheckResult EvaluateForbiddenLabels(PrCheck check, PrMetadata pr)
        {
            var forbiddenLabels = ParseStringList(check.Value);
            var prLabels = pr.Labels ?? new List<string>();
            var hasForbidden = forbiddenLabels.Where(f => 
                prLabels.Any(l => l.Equals(f, StringComparison.OrdinalIgnoreCase))).ToList();

            var passed = !hasForbidden.Any();

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? "PR has no forbidden labels" 
                    : $"PR has forbidden labels: {string.Join(", ", hasForbidden)}. Remove these before merging."
            };
        }

        private PrCheckResult EvaluateBranchPattern(PrCheck check, PrMetadata pr)
        {
            var pattern = check.Value?.ToString() ?? "";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var passed = regex.IsMatch(pr.SourceBranch ?? "");

            return new PrCheckResult
            {
                Passed = passed,
                Message = passed 
                    ? "Branch name follows naming convention" 
                    : $"Branch '{pr.SourceBranch}' does not match pattern '{pattern}'. {check.Description}"
            };
        }

        private List<string> ParseStringList(object value)
        {
            if (value == null) return new List<string>();
            
            if (value is List<object> objList)
                return objList.Select(o => o?.ToString() ?? "").ToList();
            
            if (value is IEnumerable<string> strList)
                return strList.ToList();

            return value.ToString()
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();
        }
    }
}
