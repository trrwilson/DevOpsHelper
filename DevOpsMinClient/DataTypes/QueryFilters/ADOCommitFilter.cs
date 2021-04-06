using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.QueryFilters
{
    public class ADOCommitFilter : ADOFilterBase
    {
        public string Id { get; set; }
        public Guid RepositoryId { get; set; }
        public DateTime Oldest { get; set; }

        public override string ToString()
        {
            return this.ToQueryString(
                ("repositoryId", this.RepositoryId),
                ("searchCriteria.fromDate", this.Oldest),
                ("searchCriteria.$top", this.MaxResults),
                ("commitId", this.Id));
        }
    }
}
