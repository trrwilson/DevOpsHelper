using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevOpsMinClient.DataTypes
{
    public class ADOTestRun
    {
        public string Outcome { get; set; }
        
        [JsonProperty("TestRunId")]
        public int RunId { get; set; }

        [JsonProperty("TestResultId")]
        public int ResultId { get; set; }

        public DateTime StartedDate { get; set; }

        public string Name { get; set; }

        [JsonProperty("automatedTestName")]
        public string FullName { get; set; }

    }
}
