using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    public enum Severity { Info, Warning, Error }

    public sealed class Pattern
    {
        public string Type { get; set; }   // "metrics" | "roslyn-inspect" | "regex" | "list"
        public object Value { get; set; }  // shape depends on Type
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
}
