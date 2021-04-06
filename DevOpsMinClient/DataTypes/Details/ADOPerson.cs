using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes.Details
{
    public class ADOPerson
    {
        public string DisplayName { get; set; }

        [JsonProperty("uniqueName")]
        public string Email { get; set; }
    }
}
