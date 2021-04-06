using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    public class ADOPullRequestCommentThread
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
        public string Status { get; set; }
        public List<ADOPullRequestComment> Comments { get; set; }
    }
}
