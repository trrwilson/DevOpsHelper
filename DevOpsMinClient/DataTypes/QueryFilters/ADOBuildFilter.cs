using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.QueryFilters
{
    public class ADOBuildFilter : ADOFilterBase
    {
        public string Branch { get; set; }
        public string Reason { get; set; }
        public int Definition { get; set; } = 0;
        public DateTime Oldest { get; set; } = DateTime.MinValue;
        public string SortOrder { get; set; }

        public override string ToString()
        {
            return this.ToQueryString(
                ("branchName", this.Branch),
                ("definitions", this.Definition),
                ("reasonFilter", this.Reason),
                ("minTime", this.Oldest),
                ("queryOrder", this.SortOrder),
                ("$top", this.MaxResults)
            );
        }
    }
}
