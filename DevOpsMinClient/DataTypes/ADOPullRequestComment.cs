using DevOpsMinClient.DataTypes.Details;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    public class ADOPullRequestComment
    {
        public int Id { get; set; }
        public ADOPerson Author { get; set; }

        public string Content { get; set; }
    }
}
