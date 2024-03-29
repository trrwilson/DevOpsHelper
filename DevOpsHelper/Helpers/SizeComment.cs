﻿using DevOpsMinClient.DataTypes;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DevOpsHelper.Helpers
{
    public class SizeComment
    {
        public string Version { get; set; } = "experimental.003";

        public string ProjectUrl { get; set; }
        public ADOCommit Commit { get; set; }

        public ADOBuild Build { get; set; }

        public List<(string Name, int ObservedSize, int ReferenceSize)> Entries { get; } = new List<(string, int, int)>();

        public bool IsSameAs(SizeComment other)
        {
            // Perform equivalency comparison purely on the delta entries to avoid "it's new!" notifications every
            // time the target merge branch changes (including in ways entirely unrelated to this change)
            return (this.Entries == null) == (other?.Entries == null)
                && this.Entries.Count == other?.Entries.Count
                && this.Entries.All(thisEntry => other.Entries.Any(otherEntry =>
                    otherEntry.Name == thisEntry.Name
                        && otherEntry.ReferenceSize == thisEntry.ReferenceSize
                        && otherEntry.ObservedSize == thisEntry.ObservedSize));
        }

        public static SizeComment Parse(string comment)
        {
            if (string.IsNullOrEmpty(comment) || !comment.Contains("Carbon Artifact Size Analysis (CASA)"))
            {
                return null;
            }

            var result = new SizeComment()
            {
                Version = "Unknown",
                Commit = new ADOCommit(),
            };

            int headerVersionStart = comment.IndexOf("--");
            int headerVersionEnd = comment.IndexOf("\n");

            if (headerVersionStart >= 0 && headerVersionEnd >= 0 && headerVersionEnd > headerVersionStart)
            {
                result.Version = comment[(headerVersionStart + 2)..headerVersionEnd].Trim();
            }

            var buildMatch = Regex.Match(comment, "\\((.*) @(.*)\\)");

            if (buildMatch.Success)
            {
                result.Commit.Id = buildMatch.Groups[2].Value;
            }

            var entryMatches = Regex.Matches(comment, "\\|(.*)\\| `([^\\|]*)` \\| `([^\\|]*)` \\| `([^\\|]*)` \\|");
            foreach (var entryMatch in entryMatches)
            {
                var match = entryMatch as Match;
                result.Entries.Add((
                    match.Groups[1].ToString().Trim(),
                    int.TryParse($"{match.Groups[2]}", out int currentSize) ? currentSize : -1,
                    int.TryParse($"{match.Groups[3]}", out int referenceSize) ? referenceSize : -1));
            }

            return result;
        }

        public override string ToString()
        {
            var commentHeader = $"## Carbon Artifact Size Analysis (CASA) -- {this.Version}";

            var table = $"| Target | Your size | Reference size | Change |";
            table += $"\n|---|---|---|---|";
            foreach (var (name, observedSize, referenceSize) in this.Entries)
            {
                var refSizeLabel = referenceSize >= 0 ? $"{referenceSize}" : $"Not found";
                var diffLabel = referenceSize >= 0 ?
                    observedSize == referenceSize ? "0" : observedSize > referenceSize ? $"+{observedSize - referenceSize}" : $"-{referenceSize - observedSize}"
                    : $" ";
                table += $"\n| {name} | `{observedSize}` | `{refSizeLabel}` | `{diffLabel}` |";
            }

            var comment = commentHeader;
            comment += "\n";

            if (this.Build == null)
            {
                comment += $"Hello! CASA was unable to find a recent reference build that matches {this.Commit.Id.Substring(0, 7)}, but here's some information about your current artifact size:\n";
            }
            else
            {
                comment += $"Hello! Your PR has artifacts that appear to match this reference build:\n";
                comment += GetReferenceBuildLine();
                comment += "(There may be newer builds; this is the oldest that matches the below)\n";
                comment += "\nHere's a comparison of sizes:\n";
                comment += table;
            }

            comment += "\nFor information and tips on understanding size impact, please see http://aka.ms/carbon/size (when that exists, anyway).";
            comment += "\n\nThis is informational only--feel free to resolve the comment.";

            return comment;
        }

        private string GetReferenceBuildLine()
        {
            string result = $"- [Build {this.Build.Id}]({this.ProjectUrl}/_build/results?buildId={this.Build.Id}&view=results) ";
            result += $"({this.Build.SourceBranch} @{this.Build.HeadCommit.Substring(0, 7)})\n";
            return result;
        }
    }
}
