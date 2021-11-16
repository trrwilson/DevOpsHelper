using DevOpsMinClient.DataTypes;
using System.Collections.Generic;

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
            return other != null && this.Version == other.Version && this.Commit.Id?.Substring(0, 7) == other.Commit.Id?.Substring(0, 7);
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


            int basisStart = comment.IndexOf("merge basis");
            var basisEnd = basisStart >= 0 ? comment.IndexOf(":\n", basisStart) : -1;

            if (basisStart >= 0 && basisEnd >= 0 && basisEnd > basisStart)
            {
                result.Commit.Id = comment[(basisStart + 11)..basisEnd].Trim();
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
                comment += $"Hello! Your PR has artifacts that appear to match one of these reference builds with merge basis {this.Commit.Id.Substring(0, 7)}:\n";
                comment += $"- [Build {this.Build.Id}]({this.ProjectUrl}/_build/results?buildId={this.Build.Id}&view=results) ({this.Build.SourceBranch} @{this.Build.HeadCommit.Substring(0, 7)})\n";

                comment += "\nHere's a comparison of sizes:\n";
                comment += table;
            }

            comment += "\nFor information and tips on understanding size impact, please see http://aka.ms/carbon/size (when that exists, anyway).";
            comment += "\n\nThis is informational only--feel free to resolve the comment.";

            return comment;
        }
    }
}
