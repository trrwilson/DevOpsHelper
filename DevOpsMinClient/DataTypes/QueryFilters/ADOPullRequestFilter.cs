using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.QueryFilters
{
    public class ADOPullRequestFilter : ADOFilterBase
    {
        public string Status { get; set; }
        public string TargetBranch { get; set; }
        public Guid TargetRepositoryId { get; set; }

        public override string ToString()
        {
            return this.ToQueryString(
                ("searchCriteria.status", this.Status),
                ("searchCriteria.targetRefName", this.TargetBranch),
                ("searchCriteria.repositoryId", this.TargetRepositoryId),
                ("$top", this.MaxResults)
            );
        }
    }
}
