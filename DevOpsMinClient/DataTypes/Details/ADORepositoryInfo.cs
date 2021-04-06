using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    public class ADORepositoryInfo
    {
        public string Url { get; set; }
        public Guid Id { get; set; }

        public override string ToString() => $"{this.Id:D}";
    }
}
