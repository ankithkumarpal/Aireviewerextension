using System;
using System.Collections.Generic;
using System.Text;

namespace AiReviewer.Shared.Models
{
    /// <summary>
    /// Statistics per rule category
    /// </summary>
    public class RuleStat
    {
        public string Rule { get; set; }
        public int Count { get; set; }
    }
}
