using System;
using System.Linq;
using System.Text;

namespace DevOpsMinClient.DataTypes.QueryFilters
{
    public class ADOTestQueryFilter : ADOFilterBase
    {
        public int Pipeline { get; set; } = 0;
        public string TestName { get; set; }
        public string Outcome { get; set; }
        public string Branch { get; set; }

        public DateTime Start { get; set; } = DateTime.MinValue;

        public override string ToString()
        {
            var builder = new StringBuilder("filter(((Workflow eq 'Build'))");

            void AppendIfPresent(string key, string value, string op = "eq", bool? quote = null)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    quote ??= !value.All(Char.IsDigit);
                    value = quote.Value ? $"'{value}'" : value;
                    builder.Append($" and {key} {op} {value}");
                }
            }
            void AppendIntIfPresent(string key, int value, string op = "eq")
                => AppendIfPresent(key, value == 0 ? "" : $"{value}", op);
            void AppendDateIfPresent(string key, DateTime value, string op = "eq")
                => AppendIfPresent(key, value == DateTime.MinValue ? "" : $"{value:o}", op, false);

            AppendIntIfPresent("Pipeline/PipelineId", this.Pipeline);
            AppendIfPresent("Test/TestName", this.TestName);
            AppendIfPresent("Outcome", this.Outcome);
            AppendDateIfPresent("StartedDate", this.Start, "ge");
            AppendIfPresent("Branch/BranchName", this.Branch);

            builder.Append(")");

            if (this.MaxResults > 0)
            {
                builder.Append($"&$top={this.MaxResults}");
            }

            return builder.ToString();
        }
    }
}
